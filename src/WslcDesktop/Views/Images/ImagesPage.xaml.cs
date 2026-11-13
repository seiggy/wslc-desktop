using WslcDesktop.Localization;
using WslcDesktop.Views.Containers;
using WslcDesktop.Views.Shared;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.WSL.Containers;
using WslcDesktop.Services;
using WslcDesktop.Data;
using WslcDesktop.Images;
using WslcDesktop.ViewModels;

namespace WslcDesktop.Views.Images;

public sealed partial class ImagesPage : Page
{
    private StoreBinding? _binding;
    private readonly System.Collections.ObjectModel.ObservableCollection<ImageItem> _items = [];

    public ImagesPage()
    {
        InitializeComponent();
        List.ItemsSource = _items;
        _sessionFilter = new SessionFilterController(SessionFilter);
        _sessionFilter.Changed += Apply;
        Loaded += (_, _) => _binding = new StoreBinding(DispatcherQueue, Refresh, [DataKind.Images], DataKind.Sessions);
        Unloaded += (_, _) =>
        {
            _binding?.Dispose();
            _binding = null;
        };
    }

    private SessionFilterController _sessionFilter = null!;
    private string? _filter => _sessionFilter.Filter;
    private ImageSnapshot? _last;

    private void Refresh()
    {
        _last = App.Host.Store.Images();
        Apply();
    }

    // Operations mark the session dirty; reading now shows the result without waiting for the scheduler.
    private async Task RefreshAsync(string session)
    {
        await App.Host.Store.RefreshAsync(DataKind.Images, session);
        Refresh();
    }

    private void Apply()
    {
        if (_last is not { } snap)
        {
            return;
        }

        _sessionFilter.Sync(snap.Sessions);
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
        EmptyText.Text = _filter is null ? UiStrings.Get("NoImagesYetPullOneToGetStarted") : UiStrings.Format("NoImagesInSession", _filter);
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
        PullText.Text = UiStrings.Format("Pulling2", uri);
        PullBar.IsIndeterminate = true;
        try
        {
            var progress = new Progress<PullProgress>(p =>
            {
                PullText.Text = p.TotalBytes > 0
                    ? $"{p.Status} {WslcHost.FormatSize((ulong)p.CurrentBytes)} / {WslcHost.FormatSize((ulong)p.TotalBytes)}"
                    : p.Status;
                if (p.TotalBytes > 0)
                {
                    PullBar.IsIndeterminate = false;
                    PullBar.Maximum = p.TotalBytes;
                    PullBar.Value = p.CurrentBytes;
                }
            });
            await App.Host.Operations.Images.PullAsync(App.Host.AppSession, uri, progress);
            PullBox.Text = string.Empty;
        }
        catch (Exception e)
        {
            await ShowErrorAsync(UiStrings.Format("CouldnTPull", uri), e);
        }
        finally
        {
            PullStatus.Visibility = Visibility.Collapsed;
            PullButton.IsEnabled = true;
        }

        await RefreshAsync(App.Host.SessionId);
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
                    Title = UiStrings.Get("CanTRunThisImageHere"),
                    Content = UiStrings.Format("ThisUntaggedImageBelongsToSessionAndContainersAre", session, App.Host.SessionId, session, session),
                    CloseButtonText = UiStrings.Get("ActionClose"),
                }.ShowAsync();
                return;
            }

            var confirm = new ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = UiStrings.Get("ImageIsInAnotherSession"),
                Content = UiStrings.Format("BelongsToSessionContainersCreatedHereRunInSo", reference, session, App.Host.SessionId, session),
                PrimaryButtonText = UiStrings.Get("ActionContinue"),
                CloseButtonText = UiStrings.Get("ActionCancel"),
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
            Title = UiStrings.Format("Created", name),
            Content = UiStrings.Get("TheContainerWasCreatedOpenItsDetails"),
            PrimaryButtonText = UiStrings.Get("ViewContainer"),
            CloseButtonText = UiStrings.Get("StayHere"),
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

        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = item.IsThisApp ? UiStrings.Format("DeleteImage", item.DisplayName) : UiStrings.Format("DeleteImageFrom", item.DisplayName, item.Session),
            Content = item.IsUnused
                ? UiStrings.Get("TheImageIsRemovedFromThisSessionThisCan")
                : UiStrings.Format("TheImageIsInUseDeletingItCanFail", item.UsedBy),
            PrimaryButtonText = UiStrings.Get("ActionDelete"),
            CloseButtonText = UiStrings.Get("ActionCancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            await App.Host.Operations.Images.DeleteAsync(App.Host.Handle(item.Session), item.Target);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(UiStrings.Format("CouldnTDeleteFrom", item.Target, item.Session), ex);
        }

        await RefreshAsync(item.Session);
    }

    private async Task ShowErrorAsync(string title, Exception e)
    {
        await new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = e.Message,
            CloseButtonText = UiStrings.Get("ActionClose"),
        }.ShowAsync();
    }
}
