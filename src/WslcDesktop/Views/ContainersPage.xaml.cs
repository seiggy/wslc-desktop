using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WslcDesktop.Compose;
using WslcDesktop.Services;

namespace WslcDesktop.Views;

public sealed partial class ContainersPage : Page
{
    private readonly System.Collections.ObjectModel.ObservableCollection<ContainerItem> _items = [];

    public ContainersPage()
    {
        InitializeComponent();
        List.ItemsSource = _items;
        Loaded += (_, _) =>
        {
            App.Host.Monitor.Updated += OnChanged;
            App.Host.Telemetry.Updated += OnChanged;
            Sync();
        };
        Unloaded += (_, _) =>
        {
            App.Host.Monitor.Updated -= OnChanged;
            App.Host.Telemetry.Updated -= OnChanged;
        };
    }

    private void OnChanged() => DispatcherQueue.TryEnqueue(Sync);

    // Update items in place so rows don't flicker or lose focus on every poll.
    private void Sync()
    {
        var monitor = App.Host.Monitor;
        var rows = monitor.Containers
            .Select(r => (Row: r, Stack: ComposeLabels.TryParse(r.Labels, out var p, out _) ? p : string.Empty))
            .OrderBy(x => x.Stack.Length > 0)
            .ThenBy(x => x.Stack, StringComparer.Ordinal)
            .ToList();
        foreach (var gone in _items.Where(i => rows.All(r => r.Row.Name != i.Name)).ToList())
        {
            _items.Remove(gone);
        }

        var previousStack = string.Empty;
        for (var n = 0; n < rows.Count; n++)
        {
            var (row, stack) = rows[n];
            var item = _items.FirstOrDefault(i => i.Name == row.Name);
            if (item is null)
            {
                item = new ContainerItem(row.Name);
                _items.Insert(Math.Min(n, _items.Count), item);
            }
            else
            {
                var current = _items.IndexOf(item);
                if (current != n)
                {
                    _items.Move(current, n);
                }
            }

            var history = App.Host.Telemetry.GetHistory(row.Name);
            item.Update(row, history.Count > 0 ? history[^1] : null, App.Host.Restart.GetInfo(row.Name));
            item.SetHeader(stack.Length > 0 && stack != previousStack
                ? $"Stack: {stack} ({rows.Count(x => x.Stack == stack)})"
                : string.Empty);
            previousStack = stack;
        }

        EmptyText.Visibility = monitor.HasLoaded && rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ErrorBar.Title = "Couldn't list containers";
        ErrorBar.Message = monitor.LastError ?? string.Empty;
        ErrorBar.IsOpen = monitor.LastError is not null;
    }

    private Task RefreshAsync() => App.Host.Monitor.RefreshAsync();

    private async void New_Click(object sender, RoutedEventArgs e)
    {
        if (await ShowNewContainerDialogAsync(XamlRoot, null) is not null)
        {
            await RefreshAsync();
        }
    }

    /// <summary>Shows the New container dialog, optionally pre-filled with an image; returns the created container's name, or null if cancelled.</summary>
    internal static async Task<string?> ShowNewContainerDialogAsync(XamlRoot xamlRoot, string? prefillImage)
    {
        string? created = null;
        IReadOnlyList<ImageRow> images = [];
        try
        {
            images = await App.Host.GetImagesAsync();
        }
        catch
        {
            // Without a local image list the user can still type an image to pull.
        }

        var imageNames = images.Select(i => i.Name).ToList();
        var image = new ComboBox
        {
            Header = "Image",
            PlaceholderText = "Pick a local image or type one to pull",
            IsEditable = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = imageNames,
        };
        if (!string.IsNullOrEmpty(prefillImage))
        {
            var index = imageNames.FindIndex(n => string.Equals(n, prefillImage, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                image.SelectedIndex = index;
            }
            else
            {
                image.Text = prefillImage;
            }
        }

        var name = new TextBox { Header = "Name", PlaceholderText = "my-container" };
        if (!string.IsNullOrEmpty(prefillImage))
        {
            name.Text = RunImage.SuggestName(prefillImage, App.Host.Monitor.Containers.Select(c => c.Name));
            name.SelectAll();
        }

        var command = new TextBox { Header = "Command (optional)", PlaceholderText = "/bin/sleep 3600" };
        var ports = new TextBox { Header = "Ports (one per line)", PlaceholderText = "8080:80", AcceptsReturn = true, MinHeight = 64, TextWrapping = TextWrapping.Wrap };
        var mounts = new TextBox { Header = "Volumes and bind mounts (one per line)", PlaceholderText = "my-data:/data", AcceptsReturn = true, MinHeight = 64, TextWrapping = TextWrapping.Wrap };
        var env = new TextBox { Header = "Environment variables (one per line)", PlaceholderText = "KEY=value or KEY=${secret:name}", AcceptsReturn = true, MinHeight = 64, TextWrapping = TextWrapping.Wrap };
        var secretNames = new List<string>();
        try
        {
            secretNames.AddRange(App.Host.Secrets.List().Select(s => s.Name));
        }
        catch
        {
            // Secrets are optional; the dialog still works without the store.
        }

        var secretPicker = new ComboBox
        {
            Header = "Use a stored secret",
            PlaceholderText = secretNames.Count > 0 ? "Add a secret as an environment variable" : "No secrets stored yet (add them on the Secrets page)",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = secretNames,
            IsEnabled = secretNames.Count > 0,
        };
        secretPicker.SelectionChanged += (_, _) =>
        {
            if (secretPicker.SelectedItem is string picked)
            {
                var variable = new string(picked.ToUpperInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray());
                env.Text = (env.Text.Length > 0 && !env.Text.EndsWith('\n') ? env.Text + "\r" : env.Text) + $"{variable}={SecretTokens.Format(picked)}";
                secretPicker.SelectedItem = null;
            }
        };
        var envNote = new TextBlock
        {
            Text = "Secret values are resolved only when the container is created and never shown here. Substring redaction elsewhere in the app is best-effort.",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["SecondaryTextStyle"],
        };
        var network = new ComboBox { Header = "Network", HorizontalAlignment = HorizontalAlignment.Stretch };
        var networkNames = new List<string> { "Default (bridge)" };
        try
        {
            networkNames.AddRange((await App.Host.GetNetworkNamesAsync()).Where(n => n != "bridge"));
        }
        catch
        {
            // The default network is always available.
        }

        network.ItemsSource = networkNames;
        network.SelectedIndex = 0;
        var restart = new ComboBox { Header = "Restart policy", ItemsSource = RestartDisplay.ModeLabels, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var retries = new NumberBox { Header = "Max restarts (0 = default limit)", Minimum = 0, Maximum = 100, Value = 0, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Visibility = Visibility.Collapsed };
        var restartNote = new TextBlock
        {
            Text = "Applied by WSLc Desktop Manager, so it only works while the app is running.",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        restart.SelectionChanged += (_, _) =>
        {
            retries.Visibility = restart.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            restartNote.Visibility = restart.SelectedIndex > 0 ? Visibility.Visible : Visibility.Collapsed;
        };
        var startAfter = new CheckBox { Content = "Start after creating", IsChecked = true };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        var bar = new ProgressBar { IsIndeterminate = true, Visibility = Visibility.Collapsed };
        var error = new InfoBar { Severity = InfoBarSeverity.Error, IsOpen = false, IsClosable = false };
        var secretsPanel = new Controls.SecretsNeededPanel(App.Host.Secrets) { ActionName = "Create" };
        var panel = new StackPanel { Spacing = 12, MinWidth = 400, Children = { image, name, command, ports, mounts, env, secretPicker, secretsPanel, envNote, network, restart, retries, restartNote, startAfter, status, bar, error } };

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "New container",
            Content = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Disabled, MaxHeight = 640, Padding = new Thickness(0, 0, 16, 0) },
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
        };

        void Validate() => dialog.IsPrimaryButtonEnabled =
            ImageText().Length > 0 && name.Text.Trim().Length > 0 && secretsPanel.IsSatisfied;

        // Tokens typed for secrets that aren't stored yet become inline "create it now" rows.
        void SyncSecrets()
        {
            HashSet<string> stored;
            try
            {
                stored = App.Host.Secrets.List().Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                stored = [];
            }

            var missing = Lines(env.Text).SelectMany(SecretTokens.Find).Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(n => !stored.Contains(n)).Select(n => new SecretNeed(n, [], false)).ToList();
            secretsPanel.SetNeeds(missing);
            Validate();
        }

        env.TextChanged += (_, _) => SyncSecrets();
        secretsPanel.StateChanged += Validate;

        dialog.Opened += (_, _) =>
        {
            Validate();
            if (prefillImage is { Length: > 0 })
            {
                name.Focus(FocusState.Programmatic);
            }
            else
            {
                image.Focus(FocusState.Programmatic);
            }
        };

        string ImageText() => (image.SelectedItem as string ?? image.Text ?? string.Empty).Trim();

        image.SelectionChanged += (_, _) => Validate();
        image.TextSubmitted += (_, a) => { image.Text = a.Text; Validate(); };
        image.RegisterPropertyChangedCallback(ComboBox.TextProperty, (_, _) => Validate());
        name.TextChanged += (_, _) => Validate();

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            var imageName = ImageText();
            var containerName = name.Text.Trim();
            var commandLine = command.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            error.IsOpen = false;
            image.IsEnabled = name.IsEnabled = command.IsEnabled = ports.IsEnabled = mounts.IsEnabled = env.IsEnabled = network.IsEnabled = restart.IsEnabled = retries.IsEnabled = startAfter.IsEnabled = false;
            dialog.IsPrimaryButtonEnabled = false;
            bar.Visibility = status.Visibility = Visibility.Visible;
            try
            {
                if (imageName != prefillImage && !HasImage(imageNames, imageName))
                {
                    status.Text = $"Pulling {imageName}…";
                    var progress = new Progress<Microsoft.WSL.Containers.ImageProgress>(p =>
                    {
                        status.Text = $"Pulling {imageName}: {p.Status}";
                        if (p.TotalBytes > 0)
                        {
                            bar.IsIndeterminate = false;
                            bar.Maximum = p.TotalBytes;
                            bar.Value = p.CurrentBytes;
                        }
                    });
                    await App.Host.PullImageAsync(imageName, progress);
                    bar.IsIndeterminate = true;
                }

                // A typed value matching a stored secret becomes its token so it is injected, not kept in plain text.
                if (!secretsPanel.Commit())
                {
                    throw new InvalidOperationException("A secret couldn't be saved; see the message under it.");
                }

                var envLines = Lines(App.Host.Secrets.Redact(env.Text));
                var stored = App.Host.Secrets.List().Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (envLines.SelectMany(SecretTokens.Find).FirstOrDefault(n => !stored.Contains(n)) is { } missing)
                {
                    throw new InvalidOperationException($"The secret '{missing}' isn't stored.");
                }

                status.Text = "Creating container…";
                await App.Host.CreateContainerAsync(new NewContainerSpec(
                    containerName, imageName, commandLine, Lines(ports.Text), Lines(mounts.Text), envLines,
                    network.SelectedIndex > 0 ? (string)network.SelectedItem : string.Empty));
                if (restart.SelectedIndex > 0)
                {
                    App.Host.SetRestartPolicy(containerName, RestartDisplay.FromSelection(restart.SelectedIndex, (int)retries.Value), startAfter.IsChecked == true);
                }

                if (startAfter.IsChecked == true)
                {
                    status.Text = "Starting container…";
                    await App.Host.StartContainerAsync(containerName);
                }

                created = containerName;
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                error.Message = ex.Message;
                error.IsOpen = true;
                image.IsEnabled = name.IsEnabled = command.IsEnabled = ports.IsEnabled = mounts.IsEnabled = env.IsEnabled = network.IsEnabled = restart.IsEnabled = retries.IsEnabled = startAfter.IsEnabled = true;
                bar.Visibility = status.Visibility = Visibility.Collapsed;
                Validate();
            }
            finally
            {
                deferral.Complete();
            }
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary ? created : null;
    }

    private static string[] Lines(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // A local image may be listed as "alpine:latest" or with a registry prefix and an implicit tag.
    private static bool HasImage(IEnumerable<string> localNames, string typed)
    {
        static string Normalize(string n)
        {
            n = n.Trim();
            foreach (var prefix in new[] { "docker.io/library/", "docker.io/" })
            {
                if (n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    n = n[prefix.Length..];
                    break;
                }
            }

            var slash = n.LastIndexOf('/');
            return n.IndexOf(':', slash + 1) < 0 ? n + ":latest" : n;
        }

        var wanted = Normalize(typed);
        return localNames.Any(n => string.Equals(Normalize(n), wanted, StringComparison.OrdinalIgnoreCase));
    }

    private void List_ItemClick(object sender, ItemClickEventArgs e) =>
        Frame.Navigate(typeof(ContainerDetailPage), ((ContainerItem)e.ClickedItem).Name);

    private async void Start_Click(object sender, RoutedEventArgs e) =>
        await ActAsync("Couldn't start the container", () => App.Host.StartContainerAsync(NameOf(sender)));

    private async void Stop_Click(object sender, RoutedEventArgs e) =>
        await ActAsync("Couldn't stop the container", () => App.Host.StopContainerAsync(NameOf(sender)));

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var name = NameOf(sender);
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Delete \"{name}\"?",
            Content = "The container is removed, including its writable layer. This can't be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await ActAsync("Couldn't delete the container", () => App.Host.DeleteContainerAsync(name));
    }

    private static string NameOf(object sender) => (string)((FrameworkElement)sender).Tag;

    private async Task ActAsync(string failureTitle, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception e)
        {
            await ShowAsync(failureTitle, e.Message);
        }

        await RefreshAsync();
    }

    private async Task ShowAsync(string title, string message)
    {
        await new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "Close",
        }.ShowAsync();
    }
}
