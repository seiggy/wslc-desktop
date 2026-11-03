using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.WSL.Containers;
using WslcDesktop.Services;

namespace WslcDesktop.Views;

public sealed partial class ImagesPage : Page
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly System.Collections.ObjectModel.ObservableCollection<ImageItem> _items = [];

    public ImagesPage()
    {
        InitializeComponent();
        List.ItemsSource = _items;
        Loaded += async (_, _) =>
        {
            _timer.Start();
            await RefreshAsync();
        };
        Unloaded += (_, _) => _timer.Stop();
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    private const string AllSessions = "All sessions";

    private string? _filter;
    private bool _updatingFilter;
    private bool _refreshing;
    private ImageSnapshot? _last;

    private async Task RefreshAsync(bool force = false)
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            _last = await App.Host.ImageCatalog.RefreshAsync(force);
            Apply();
        }
        catch (Exception e)
        {
            _timer.Stop();
            await ShowErrorAsync("Couldn't list images", e);
            _timer.Start();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void Apply()
    {
        if (_last is not { } snap)
        {
            return;
        }

        SyncFilter(snap.Sessions);
        var images = snap.Images
            .Where(i => _filter is null || string.Equals(i.Session, _filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Update rows in place so the list never flickers or loses scroll/focus on a poll.
        foreach (var gone in _items.Where(i => images.All(n => n.Key != i.Key)).ToList())
        {
            _items.Remove(gone);
        }

        for (var n = 0; n < images.Count; n++)
        {
            var item = _items.FirstOrDefault(i => i.Key == images[n].Key);
            if (item is null)
            {
                _items.Insert(Math.Min(n, _items.Count), new ImageItem(images[n]));
            }
            else
            {
                item.Update(images[n]);
                var at = _items.IndexOf(item);
                if (at != n && n < _items.Count)
                {
                    _items.Move(at, n);
                }
            }
        }

        var failed = snap.Failures.Count > 0
            ? string.Join('\n', snap.Failures.Select(f => $"{f.Session}: {f.Message}"))
            : string.Empty;
        if (SessionErrorBar.Message != failed)
        {
            SessionErrorBar.Message = failed;
        }

        SessionErrorBar.IsOpen = failed.Length > 0;
        EmptyText.Visibility = images.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _filter is null ? "No images yet. Pull one to get started." : $"No images in session \"{_filter}\".";
    }

    private void SyncFilter(IReadOnlyList<string> sessions)
    {
        var wanted = new List<string> { AllSessions };
        wanted.AddRange(sessions.Select(s => App.Host.IsAppSession(s) ? $"{s} (this app)" : s));
        var current = SessionFilter.Items.Cast<string>().ToList();
        _updatingFilter = true;
        try
        {
            if (!current.SequenceEqual(wanted))
            {
                for (var i = current.Count - 1; i >= 0; i--)
                {
                    if (!wanted.Contains(current[i]))
                    {
                        SessionFilter.Items.RemoveAt(i);
                    }
                }

                for (var i = 0; i < wanted.Count; i++)
                {
                    if (i >= SessionFilter.Items.Count || (string)SessionFilter.Items[i] != wanted[i])
                    {
                        SessionFilter.Items.Insert(i, wanted[i]);
                    }
                }
            }

            if (_filter is not null && !sessions.Contains(_filter, StringComparer.OrdinalIgnoreCase))
            {
                _filter = null;
            }

            var label = _filter is null ? AllSessions : wanted[1 + sessions.ToList().FindIndex(s => string.Equals(s, _filter, StringComparison.OrdinalIgnoreCase))];
            if (!Equals(SessionFilter.SelectedItem, label))
            {
                SessionFilter.SelectedItem = label;
            }
        }
        finally
        {
            _updatingFilter = false;
        }
    }

    private void SessionFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingFilter || SessionFilter.SelectedIndex < 0 || _last is null)
        {
            return;
        }

        _filter = SessionFilter.SelectedIndex == 0 ? null : _last.Sessions[SessionFilter.SelectedIndex - 1];
        Apply();
    }

    private void List_ItemClick(object sender, ItemClickEventArgs e)
    {
        var item = (ImageItem)e.ClickedItem;
        Frame.Navigate(typeof(ImageDetailPage), new ImageRef(item.Session, item.Target));
    }
    private async void PullBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            await PullAsync();
        }
    }

    private async void Pull_Click(object sender, RoutedEventArgs e) => await PullAsync();

    private async Task PullAsync()
    {
        var uri = PullBox.Text.Trim();
        if (uri.Length == 0)
        {
            return;
        }

        PullButton.IsEnabled = false;
        PullStatus.Visibility = Visibility.Visible;
        PullText.Text = $"Pulling {uri}…";
        PullBar.IsIndeterminate = true;
        try
        {
            var progress = new Progress<ImageProgress>(p =>
            {
                PullText.Text = $"{p.Status} {p.Id}";
                if (p.TotalBytes > 0)
                {
                    PullBar.IsIndeterminate = false;
                    PullBar.Maximum = p.TotalBytes;
                    PullBar.Value = p.CurrentBytes;
                }
            });
            await App.Host.PullImageAsync(uri, progress);
            App.Host.ImageCatalog.Invalidate(App.Host.SessionId);
            PullBox.Text = string.Empty;
        }
        catch (Exception e)
        {
            await ShowErrorAsync($"Couldn't pull {uri}", e);
        }
        finally
        {
            PullStatus.Visibility = Visibility.Collapsed;
            PullButton.IsEnabled = true;
        }

        await RefreshAsync();
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not ImageItem item)
        {
            return;
        }

        await RunFromAsync(XamlRoot, Frame, item.Session, item.RunReference, item.CanCopyToApp);
    }

    /// <summary>
    /// Opens the New container dialog for an image. Containers are always created in the app's session, so an image
    /// owned by another session is pulled into the app's session first (after confirmation); untagged images can't be.
    /// </summary>
    internal static async Task RunFromAsync(XamlRoot xamlRoot, Frame frame, string session, string reference, bool canCopy)
    {
        var inApp = App.Host.IsAppSession(session);
        if (!inApp)
        {
            if (!canCopy)
            {
                await new ContentDialog
                {
                    XamlRoot = xamlRoot,
                    Title = "Can't run this image here",
                    Content = $"This untagged image belongs to session \"{session}\", and containers are created in \"{App.Host.SessionId}\". Untagged images have no reference to pull, so tag it in \"{session}\" first, or run it there with wslc --session {session}.",
                    CloseButtonText = "Close",
                }.ShowAsync();
                return;
            }

            var confirm = new ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = "Image is in another session",
                Content = $"\"{reference}\" belongs to session \"{session}\". Containers created here run in \"{App.Host.SessionId}\", so the image will be pulled into that session from its registry (a locally built image can't be). The copy in \"{session}\" is left untouched.",
                PrimaryButtonText = "Continue",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }

        var created = await ContainersPage.ShowNewContainerDialogAsync(xamlRoot, reference, inApp);
        if (created is not null)
        {
            await OfferOpenContainerAsync(xamlRoot, frame, created);
        }
    }
    /// <summary>After a container is created, offers to open its details.</summary>
    internal static async Task OfferOpenContainerAsync(XamlRoot xamlRoot, Frame frame, string name)
    {
        var done = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = $"Created \"{name}\"",
            Content = "The container was created. Open its details?",
            PrimaryButtonText = "View container",
            CloseButtonText = "Stay here",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await done.ShowAsync() == ContentDialogResult.Primary)
        {
            frame.Navigate(typeof(ContainerDetailPage), name);
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not ImageItem item)
        {
            return;
        }

        try
        {
            await App.Host.DeleteImageAsync(item.Session, item.Target);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync($"Couldn't delete {item.Target} from {item.Session}", ex);
        }

        await RefreshAsync(true);
    }

    private async Task ShowErrorAsync(string title, Exception e)
    {
        await new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = e.Message,
            CloseButtonText = "Close",
        }.ShowAsync();
    }
}
