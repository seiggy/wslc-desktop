using WslcDesktop.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WslcDesktop.Controls;
using WslcDesktop.Lifecycle;
using WslcDesktop.Lifecycle.Startup;

namespace WslcDesktop.Views.Settings.Tabs;

/// <summary>How the app behaves on the desktop: starting with Windows and living in the tray.</summary>
public sealed class BehaviorTab : SettingsTab
{
    public BehaviorTab(StatusReporter report) : base(report)
    {
        var settings = App.Host.Settings;
        var card = InspectUi.Card(UiStrings.Get("StartupAndTray"), out var rows);
        rows.Spacing = 12;
        rows.Children.Add(InspectUi.Description(UiStrings.Get("HowWSLcDesktopManagerBehavesOnYourDesktopChanges")));

        AddStartupPreference(rows);
        AddPreference(
            rows,
            UiStrings.Get("MinimizeToTray"),
            UiStrings.Get("MinimizingHidesTheWindowToTheNotificationAreaAnd"),
            settings.MinimizeToTray,
            on => settings.MinimizeToTray = on);
        AddPreference(
            rows,
            UiStrings.Get("CloseButtonMinimizesToTray"),
            UiStrings.Get("TheCloseButtonXHidesTheWindowToThe"),
            settings.CloseMinimizesToTray,
            on => settings.CloseMinimizesToTray = on);
        Children.Add(card);
    }

    // Start with Windows reads and writes the OS (Run key or StartupTask), so it always reflects what Windows will do.
    private void AddStartupPreference(StackPanel rows)
    {
        var toggle = new ToggleSwitch { Header = UiStrings.Get("StartWithWindows"), IsEnabled = false };
        var help = Help(string.Empty);
        var windowsSettings = new HyperlinkButton
        {
            Content = UiStrings.Get("OpenWindowsStartupSettings"),
            NavigateUri = new Uri("ms-settings:startupapps"),
            Padding = new Thickness(0),
            Visibility = Visibility.Collapsed,
        };
        var loading = true;

        void Show(StartupStatus status)
        {
            loading = true;
            toggle.IsOn = status.IsOn;
            toggle.IsEnabled = status.CanToggle;
            loading = false;
            help.Text = StartupHelp.Describe(status, TrayPolicy.ShowTrayIcon(App.Host.Settings));
            windowsSettings.Visibility = status.NeedsWindowsSettings ? Visibility.Visible : Visibility.Collapsed;
        }

        toggle.Toggled += async (_, _) =>
        {
            if (loading)
            {
                return;
            }

            toggle.IsEnabled = false;
            Show(await Platform.StartupRegistration.SetEnabledAsync(toggle.IsOn));
        };
        rows.Children.Add(new StackPanel { Spacing = 4, Children = { toggle, help, windowsSettings } });
        _ = Platform.StartupRegistration.GetStatusAsync().ContinueWith(
            t => DispatcherQueue.TryEnqueue(() => Show(t.Result)), TaskScheduler.Default);
    }

    private void AddPreference(StackPanel rows, string header, string help, bool value, Action<bool> set)
    {
        var toggle = new ToggleSwitch { Header = header, IsOn = value };
        toggle.Toggled += (_, _) =>
        {
            set(toggle.IsOn);
            try
            {
                App.Host.Settings.Save();
                App.Tray?.Apply();
            }
            catch (Exception ex)
            {
                Report(UiStrings.Get("CouldnTSaveAppSettings"), ex.Message, InfoBarSeverity.Error);
            }
        };
        rows.Children.Add(new StackPanel { Spacing = 4, Children = { toggle, Help(help) } });
    }

    private static TextBlock Help(string text)
    {
        var block = InspectUi.Description(text);
        block.FontSize = 12;
        return block;
    }
}
