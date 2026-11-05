using WslcDesktop.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.WSL.Containers;
using WslcDesktop.Views.Containers;
using WslcDesktop.Views.Stacks;
using WslcDesktop.Views.Images;
using WslcDesktop.Views.Resources;
using WslcDesktop.Views.Secrets;
using WslcDesktop.Views.Sessions;
using WslcDesktop.Views.Settings;
using WslcDesktop.Resources;

namespace WslcDesktop;

public sealed partial class MainWindow : Window
{
    private IReadOnlyList<Component> _missing = [];

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ApplyAppIcon();
        Nav.IsPaneVisible = false;
        _ = BootAsync();
    }

    private void ApplyAppIcon()
    {
        // Taskbar / Alt-Tab / window icon, and the 16 DIP title bar icon (scale-qualified PNGs resolved by MRT).
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        AppTitleBar.IconSource = new ImageIconSource
        {
            ImageSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/TitleBarIcon.png")),
        };
    }

    private async Task BootAsync()
    {
        ShowStartup(UiStrings.Get("StartingWSLc"), string.Empty, busy: true);
        try
        {
            _missing = await App.Host.GetMissingComponentsAsync();
            if (_missing.Contains(Component.SdkNeedsUpdate))
            {
                ShowStartup(
                    UiStrings.Get("UpdateRequired"),
                    UiStrings.Get("ThisAppIsIncompatibleWithTheInstalledWSLRuntime"),
                    busy: false,
                    retry: true);
                return;
            }

            if (_missing.Count > 0)
            {
                ShowStartup(
                    UiStrings.Get("WSLComponentsAreMissing"),
                    UiStrings.Format("MissingComponents", string.Join(", ", _missing)),
                    busy: false,
                    install: true);
                return;
            }

            await App.Host.StartSessionAsync();
            var version = await App.Host.GetVersionAsync();
            AppTitleBar.Subtitle = UiStrings.Format("WSL", version);
            StartupPanel.Visibility = Visibility.Collapsed;
            Nav.IsPaneVisible = true;
            Nav.SelectedItem = Nav.MenuItems[0];
        }
        catch (Exception e)
        {
            ShowStartup(UiStrings.Get("CouldnTStartTheWSLcSession"), e.Message, busy: false, retry: true);
        }
    }

    private void ShowStartup(string title, string detail, bool busy, bool install = false, bool retry = false)
    {
        StartupPanel.Visibility = Visibility.Visible;
        StartupTitle.Text = title;
        StartupDetail.Text = detail;
        StartupProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        InstallButton.Visibility = install ? Visibility.Visible : Visibility.Collapsed;
        RetryButton.Visibility = retry ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        ShowStartup(UiStrings.Get("InstallingComponents"), string.Empty, busy: true);
        try
        {
            var progress = new Progress<InstallProgress>(p =>
                StartupDetail.Text = $"{p.Component}: {p.Progress}/{p.Total}");
            await App.Host.InstallMissingAsync(_missing, progress);
        }
        catch (Exception ex)
        {
            ShowStartup(UiStrings.Get("InstallFailed"), ex.Message, busy: false, retry: true);
            return;
        }

        await BootAsync();
    }

    private void Retry_Click(object sender, RoutedEventArgs e) => _ = BootAsync();

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            ContentFrame.Navigate(typeof(SettingsPage));
        }
        else if (args.SelectedItem is NavigationViewItem { Tag: string tag })
        {
            ContentFrame.Navigate(tag switch
            {
                "images" => typeof(ImagesPage),
                "stacks" => typeof(StacksPage),
                "volumes" or "networks" => typeof(ResourcesPage),
                "secrets" => typeof(SecretsPage),
                "sessions" => typeof(SessionsPage),
                _ => typeof(ContainersPage),
            }, tag switch
            {
                "volumes" => ResourceKind.Volume,
                "networks" => ResourceKind.Network,
                _ => null,
            });
        }
    }
}
