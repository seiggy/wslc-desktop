using WslcDesktop.Views.Shared;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WslcDesktop.Services;
using WslcDesktop.Resources;
using WslcDesktop.Sessions;
using WslcDesktop.ViewModels;

namespace WslcDesktop.Views.Resources;

/// <summary>Lists volumes or networks; the navigation parameter selects which.</summary>
public sealed partial class ResourcesPage : Page
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly System.Collections.ObjectModel.ObservableCollection<ResourceItem> _items = [];
    private ResourceKind _kind;
    private bool _busy;
    private readonly SessionFilterController _sessionFilter;
    private SessionSnapshot<ResourceRow>? _snapshot;

    private string Noun => _kind == ResourceKind.Volume ? "volume" : "network";

    public ResourcesPage()
    {
        InitializeComponent();
        List.ItemsSource = _items;
        _sessionFilter = new SessionFilterController(SessionFilter);
        _sessionFilter.Changed += Apply;
        Loaded += async (_, _) =>
        {
            _timer.Start();
            await RefreshAsync();
        };
        Unloaded += (_, _) => _timer.Stop();
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _kind = e.Parameter is ResourceKind k ? k : ResourceKind.Volume;
        TitleText.Text = _kind == ResourceKind.Volume ? "Volumes" : "Networks";
        NewButton.Content = $"New {Noun}";
        Col3Header.Text = _kind == ResourceKind.Volume ? "Scope" : "Access";
    }

    private async Task RefreshAsync(bool force = false)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            _snapshot = await App.Host.ResourceCatalog(_kind).RefreshAsync(force);
            if (ErrorBar.IsOpen)
            {
                ErrorBar.IsOpen = false;
            }

            Apply();
        }
        catch (Exception e)
        {
            ErrorBar.Title = $"Couldn't list {Noun}s";
            ErrorBar.Message = e.Message;
            ErrorBar.IsOpen = true;
        }
        finally
        {
            _busy = false;
        }
    }

    // Update rows in place so the list never flickers or loses scroll/focus on a poll.
    private void Apply()
    {
        if (_snapshot is not { } snap)
        {
            return;
        }

        _sessionFilter.Sync(snap.Sessions);
        var rows = snap.Items
            .Where(e => _sessionFilter.Filter is null || string.Equals(e.Session, _sessionFilter.Filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
        static string KeyOf(SessionEntry<ResourceRow> e) => $"{e.Session}|{e.Item.Name}";
        foreach (var gone in _items.Where(i => rows.All(r => KeyOf(r) != i.Key)).ToList())
        {
            _items.Remove(gone);
        }

        for (var n = 0; n < rows.Count; n++)
        {
            var item = _items.FirstOrDefault(i => i.Key == KeyOf(rows[n]));
            if (item is null)
            {
                _items.Insert(Math.Min(n, _items.Count), new ResourceItem(rows[n]));
            }
            else
            {
                item.Update(rows[n].Item);
                var at = _items.IndexOf(item);
                if (at != n && n < _items.Count)
                {
                    _items.Move(at, n);
                }
            }
        }

        var failed = string.Join('\n', snap.Failures.Select(f => $"{f.Session}: {f.Message}"));
        if (SessionErrorBar.Message != failed)
        {
            SessionErrorBar.Message = failed;
        }

        SessionErrorBar.IsOpen = failed.Length > 0;
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _sessionFilter.Filter is not null
            ? $"No {Noun}s in session \"{_sessionFilter.Filter}\"."
            : _kind == ResourceKind.Volume ? "No volumes yet. Create one to persist container data." : "No networks.";
        PruneButton.Content = _sessionFilter.Filter is null || App.Host.IsAppSession(_sessionFilter.Filter) ? "Prune unused" : $"Prune unused in {_sessionFilter.Filter}";
    }

    private string PruneSession => _sessionFilter.Filter ?? App.Host.SessionId;

    private void List_ItemClick(object sender, ItemClickEventArgs e)
        { var item = (ResourceItem)e.ClickedItem; Frame.Navigate(typeof(ResourceDetailPage), new ResourceRef(_kind, item.Session, item.Name)); }

    private async void New_Click(object sender, RoutedEventArgs e)
    {
        var sessions = _snapshot?.Sessions ?? [App.Host.SessionId];
        var sessionPicker = new ComboBox
        {
            Header = "Session",
            ItemsSource = sessions.Select(s => App.Host.IsAppSession(s) ? $"{s} (this app)" : s).ToList(),
            SelectedIndex = Math.Max(0, sessions.ToList().FindIndex(s => string.Equals(s, _sessionFilter.Filter ?? App.Host.SessionId, StringComparison.OrdinalIgnoreCase))),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = sessions.Count > 1,
        };
        var name = new TextBox { Header = "Name", PlaceholderText = _kind == ResourceKind.Volume ? "my-data" : "my-network" };
        var driver = new ComboBox { Header = "Driver", HorizontalAlignment = HorizontalAlignment.Stretch };
        var subnet = new TextBox { Header = "Subnet (optional)", PlaceholderText = "10.10.0.0/24" };
        var gateway = new TextBox { Header = "Gateway (optional)", PlaceholderText = "10.10.0.1" };
        var size = new NumberBox
        {
            Header = "Size",
            Value = VolumeSize.DefaultGb,
            Minimum = 0,
            Maximum = 1024 * 1024,
            SmallChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var unit = new ComboBox { Header = "Unit", ItemsSource = VolumeSize.UnitLabels, SelectedIndex = 1, MinWidth = 90 };
        var sizeRow = new Grid { ColumnSpacing = 8, Visibility = Visibility.Collapsed };
        sizeRow.ColumnDefinitions.Add(new ColumnDefinition());
        sizeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(unit, 1);
        sizeRow.Children.Add(size);
        sizeRow.Children.Add(unit);
        var sizeNote = new TextBlock
        {
            Text = "A VHD volume is a virtual disk with a fixed maximum size (3 MB to 64 TB). It only uses host disk space as data is written.",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Visibility = Visibility.Collapsed,
        };
        var isInternal = new CheckBox { Content = "Internal (no external access)" };
        var error = new InfoBar { Severity = InfoBarSeverity.Error, IsOpen = false, IsClosable = false };
        var panel = new StackPanel { Spacing = 12, MinWidth = 380 };
        panel.Children.Add(sessionPicker);
        panel.Children.Add(name);
        panel.Children.Add(driver);
        if (_kind == ResourceKind.Volume)
        {
            driver.ItemsSource = new[] { "guest", "vhd" };
            driver.SelectedIndex = 0;
            panel.Children.Add(sizeRow);
            panel.Children.Add(sizeNote);
            driver.SelectionChanged += (_, _) =>
            {
                sizeRow.Visibility = sizeNote.Visibility = driver.SelectedItem as string == "vhd" ? Visibility.Visible : Visibility.Collapsed;
            };
        }
        else
        {
            driver.ItemsSource = new[] { "bridge" };
            driver.SelectedIndex = 0;
            panel.Children.Add(subnet);
            panel.Children.Add(gateway);
            panel.Children.Add(isInternal);
        }

        panel.Children.Add(error);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"New {Noun}",
            Content = panel,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
        };
        name.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = name.Text.Trim().Length > 0;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                var n = name.Text.Trim();
                var d = (string)driver.SelectedItem;
                var target = sessions[Math.Max(0, sessionPicker.SelectedIndex)];
                _creationSession = target;
                if (_kind == ResourceKind.Volume)
                {
                    long? bytes = null;
                    if (d == "vhd")
                    {
                        if (!VolumeSize.TryToBytes(size.Value, (SizeUnit)unit.SelectedIndex, out var parsed, out var problem))
                        {
                            throw new InvalidOperationException(problem);
                        }

                        bytes = parsed;
                    }

                    await App.Host.Operations.Resources.CreateVolumeAsync(App.Host.Handle(target), n, d, bytes);
                }
                else
                {
                    await App.Host.Operations.Resources.CreateNetworkAsync(App.Host.Handle(target), n, d, subnet.Text.Trim(), gateway.Text.Trim(), isInternal.IsChecked == true);
                }
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                error.Message = ex.Message;
                error.IsOpen = true;
            }
            finally
            {
                deferral.Complete();
            }
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            ResourceCatalogInvalidate();
            await RefreshAsync(true);
        }
    }

    private void ResourceCatalogInvalidate() => App.Host.ResourceCatalog(_kind).Invalidate(_creationSession);

    private string _creationSession = string.Empty;

    private async void Prune_Click(object sender, RoutedEventArgs e)
    {
        var what = _kind == ResourceKind.Volume ? "volumes that no container uses" : "networks that no container uses";
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Prune unused {Noun}s in {PruneSession}?",
            Content = $"This removes all {what} in session \"{PruneSession}\". This can't be undone.",
            PrimaryButtonText = "Prune",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await ActAsync($"Couldn't prune {Noun}s", () => App.Host.Operations.Resources.PruneAsync(App.Host.Handle(PruneSession), _kind));
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        var target = (ResourceItem)((FrameworkElement)sender).Tag;
        var name = target.Name;
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = target.IsThisApp ? $"Remove {Noun} \"{name}\"?" : $"Remove {Noun} \"{name}\" from {target.Session}?",
            Content = _kind == ResourceKind.Volume
                ? "The volume and all the data in it will be deleted. This can't be undone."
                : "The network will be removed. Containers must be disconnected first.",
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await ActAsync($"Couldn't remove {name}", () => App.Host.Operations.Resources.RemoveAsync(App.Host.Handle(target.Session), _kind, name));
    }

    private async Task ActAsync(string failureTitle, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            await new ContentDialog { XamlRoot = XamlRoot, Title = failureTitle, Content = ex.Message, CloseButtonText = "Close" }.ShowAsync();
        }

        await RefreshAsync(true);
    }
}
