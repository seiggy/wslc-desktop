using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        var rows = monitor.Containers;
        foreach (var gone in _items.Where(i => rows.All(r => r.Name != i.Name)).ToList())
        {
            _items.Remove(gone);
        }

        foreach (var row in rows)
        {
            var item = _items.FirstOrDefault(i => i.Name == row.Name);
            if (item is null)
            {
                item = new ContainerItem(row.Name);
                _items.Add(item);
            }

            var history = App.Host.Telemetry.GetHistory(row.Name);
            item.Update(row, history.Count > 0 ? history[^1] : null);
        }

        EmptyText.Visibility = monitor.HasLoaded && rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ErrorBar.Title = "Couldn't list containers";
        ErrorBar.Message = monitor.LastError ?? string.Empty;
        ErrorBar.IsOpen = monitor.LastError is not null;
    }

    private Task RefreshAsync() => App.Host.Monitor.RefreshAsync();

    private async void New_Click(object sender, RoutedEventArgs e)
    {
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
        var name = new TextBox { Header = "Name", PlaceholderText = "my-container" };
        var command = new TextBox { Header = "Command (optional)", PlaceholderText = "/bin/sleep 3600" };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        var bar = new ProgressBar { IsIndeterminate = true, Visibility = Visibility.Collapsed };
        var error = new InfoBar { Severity = InfoBarSeverity.Error, IsOpen = false, IsClosable = false };
        var panel = new StackPanel { Spacing = 12, MinWidth = 400, Children = { image, name, command, status, bar, error } };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "New container",
            Content = panel,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
        };

        void Validate() => dialog.IsPrimaryButtonEnabled =
            ImageText().Length > 0 && name.Text.Trim().Length > 0;

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
            image.IsEnabled = name.IsEnabled = command.IsEnabled = false;
            dialog.IsPrimaryButtonEnabled = false;
            bar.Visibility = status.Visibility = Visibility.Visible;
            try
            {
                if (!HasImage(imageNames, imageName))
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

                status.Text = "Creating container…";
                await App.Host.CreateContainerAsync(containerName, imageName, commandLine);
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                error.Message = ex.Message;
                error.IsOpen = true;
                image.IsEnabled = name.IsEnabled = command.IsEnabled = true;
                bar.Visibility = status.Visibility = Visibility.Collapsed;
                Validate();
            }
            finally
            {
                deferral.Complete();
            }
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await RefreshAsync();
        }
    }

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

    private async void Delete_Click(object sender, RoutedEventArgs e) =>
        await ActAsync("Couldn't delete the container", () => App.Host.DeleteContainerAsync(NameOf(sender)));

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
