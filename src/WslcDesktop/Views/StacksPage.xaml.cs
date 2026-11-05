using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WslcDesktop.Compose;
using WslcDesktop.Services;
using WslcDesktop.Compose.Execution;
using WslcDesktop.Compose.Parsing;
using WslcDesktop.Compose.Planning;
using WslcDesktop.Restart;
using WslcDesktop.ViewModels;

namespace WslcDesktop.Views;

public sealed partial class StacksPage : Page
{
    private readonly System.Collections.ObjectModel.ObservableCollection<StackItem> _items = [];

    public StacksPage()
    {
        InitializeComponent();
        List.ItemsSource = _items;
        Loaded += (_, _) =>
        {
            App.Host.Monitor.Updated += OnChanged;
            Sync();
        };
        Unloaded += (_, _) => App.Host.Monitor.Updated -= OnChanged;
    }

    private void OnChanged() => DispatcherQueue.TryEnqueue(Sync);

    private void Sync()
    {
        var monitor = App.Host.Monitor;
        var groups = StackItem.Group(monitor.Containers);
        foreach (var gone in _items.Where(i => groups.All(g => g.Project != i.Name)).ToList())
        {
            _items.Remove(gone);
        }

        for (var n = 0; n < groups.Count; n++)
        {
            var (project, rows) = groups[n];
            var item = _items.FirstOrDefault(i => i.Name == project);
            if (item is null)
            {
                item = new StackItem(project);
                _items.Insert(Math.Min(n, _items.Count), item);
            }
            else if (_items.IndexOf(item) != n)
            {
                _items.Move(_items.IndexOf(item), n);
            }

            item.Update(rows);
        }

        EmptyText.Visibility = monitor.HasLoaded && groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void List_ItemClick(object sender, ItemClickEventArgs e) =>
        Frame.Navigate(typeof(StackDetailPage), ((StackItem)e.ClickedItem).Name);

    private static string NameOf(object sender) => (string)((FrameworkElement)sender).Tag;

    private async void Start_Click(object sender, RoutedEventArgs e) =>
        await ActAsync("Couldn't start the stack", c => c.StartAsync(NameOf(sender)));

    private async void Stop_Click(object sender, RoutedEventArgs e) =>
        await ActAsync("Couldn't stop the stack", c => c.StopAsync(NameOf(sender)));

    private async void Down_Click(object sender, RoutedEventArgs e)
    {
        var name = NameOf(sender);
        var volumes = new CheckBox { Content = "Also remove the stack's volumes (deletes data)" };
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Take down \"{name}\"?",
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "All containers of this stack are stopped and removed, and its networks are deleted. This can't be undone.", TextWrapping = TextWrapping.Wrap },
                    volumes,
                },
            },
            PrimaryButtonText = "Down",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var removeVolumes = volumes.IsChecked == true;
        await ActAsync("Couldn't take the stack down", c => c.DownAsync(name, removeVolumes));
    }

    private async Task ActAsync(string failureTitle, Func<ComposeRunner, Task> action)
    {
        try
        {
            await action(App.Host.CreateComposeRunner(_ => { }));
        }
        catch (Exception e)
        {
            ErrorBar.Title = failureTitle;
            ErrorBar.Message = e.Message;
            ErrorBar.IsOpen = true;
        }

        await App.Host.Monitor.RefreshAsync();
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        foreach (var ext in new[] { ".yaml", ".yml" })
        {
            picker.FileTypeFilter.Add(ext);
        }

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        await PreviewAsync(file.Path);
    }

    private async Task PreviewAsync(string path)
    {
        string yaml;
        var directory = Path.GetDirectoryName(path)!;
        var env = new Dictionary<string, string>();
        try
        {
            yaml = await File.ReadAllTextAsync(path);
            var dotEnv = Path.Combine(directory, ".env");
            if (File.Exists(dotEnv))
            {
                foreach (var (k, v) in Interpolator.ParseEnvFile(await File.ReadAllTextAsync(dotEnv)))
                {
                    env[k] = v;
                }
            }

            foreach (System.Collections.DictionaryEntry kv in Environment.GetEnvironmentVariables())
            {
                env[(string)kv.Key] = (string?)kv.Value ?? string.Empty;
            }
        }
        catch (Exception ex)
        {
            ErrorBar.Title = "Couldn't read the compose file";
            ErrorBar.Message = ex.Message;
            ErrorBar.IsOpen = true;
            return;
        }

        var projectBox = new TextBox { Header = "Project name", MinWidth = 400 };
        var summary = new StackPanel { Spacing = 8 };
        var secretsPanel = new Controls.SecretsNeededPanel(App.Host.Secrets) { EnvLookup = n => env.GetValueOrDefault(n) };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        var bar = new ProgressBar { IsIndeterminate = true, Visibility = Visibility.Collapsed };
        var log = new Controls.OutputView { Height = 160, Visibility = Visibility.Collapsed };
        var panel = new StackPanel { Spacing = 12, MinWidth = 440, Children = { projectBox, secretsPanel, summary, status, bar, log } };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Up: {Path.GetFileName(path)}",
            Content = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Disabled, MaxHeight = 560, Padding = new Thickness(0, 0, 16, 0) },
            PrimaryButtonText = "Up",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        ComposePlan? plan = null;
        var running = false;
        void Replan()
        {
            var name = projectBox.Text.Trim();
            var stored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var s in App.Host.Secrets.List())
                {
                    stored.Add(s.Name);
                }
            }
            catch
            {
                // Unreadable store: every reference shows as missing.
            }

            plan = ComposePlanner.Plan(yaml, new ComposeOptions { ProjectName = name.Length > 0 ? name : null, Directory = directory, Env = env, SecretExists = stored.Contains });
            summary.Children.Clear();
            summary.Children.Add(new TextBlock { Text = $"Planned actions ({plan.Actions.Count})", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
            foreach (var a in plan.Actions)
            {
                summary.Children.Add(new TextBlock { Text = "• " + App.Host.Secrets.Redact(a.Description), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 0, 0, 0) });
            }

            foreach (var w in plan.Warnings)
            {
                summary.Children.Add(new InfoBar { Severity = InfoBarSeverity.Warning, IsOpen = true, IsClosable = false, Message = w });
            }

            foreach (var err in plan.Errors)
            {
                summary.Children.Add(new InfoBar { Severity = InfoBarSeverity.Error, IsOpen = true, IsClosable = false, Message = err });
            }

            secretsPanel.SetNeeds(plan.SecretNeeds);
            UpdatePrimary();
        }

        void UpdatePrimary() =>
            dialog.IsPrimaryButtonEnabled = plan is not null && !running && plan.Errors.Count == 0 && plan.Actions.Count > 0 && secretsPanel.IsSatisfied;

        secretsPanel.StateChanged += UpdatePrimary;
        Replan();
        projectBox.Text = plan!.Project;
        projectBox.LostFocus += (_, _) => Replan();

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            if (!secretsPanel.Commit())
            {
                args.Cancel = true;
                deferral.Complete();
                return;
            }

            Replan();
            if (plan is null || !plan.CanRun)
            {
                args.Cancel = true;
                deferral.Complete();
                return;
            }

            running = true;
            projectBox.IsEnabled = secretsPanel.IsEnabled = false;
            dialog.IsPrimaryButtonEnabled = false;
            bar.Visibility = status.Visibility = log.Visibility = Visibility.Visible;
            status.Text = "Bringing the stack up…";
            try
            {
                await App.Host.CreateComposeRunner(log.Append).UpAsync(plan);
                foreach (var action in plan.Actions.Where(a => a.Kind == ActionKind.RunService && a.Restart is not null))
                {
                    App.Host.SetRestartPolicy(action.Target, RestartPolicy.Parse(action.Restart), running: true);
                }
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                status.Text = "Failed: " + ex.Message;
                bar.Visibility = Visibility.Collapsed;
                projectBox.IsEnabled = secretsPanel.IsEnabled = true;
                dialog.IsPrimaryButtonEnabled = true;
                running = false;
            }
            finally
            {
                deferral.Complete();
            }
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && running)
        {
            await App.Host.Monitor.RefreshAsync();
        }
    }
}
