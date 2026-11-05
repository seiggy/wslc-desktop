using Microsoft.UI.Xaml.Controls;
using WslcDesktop.Views.SettingsTabs;

namespace WslcDesktop.Views;

/// <summary>Settings, split into tabs by concern. Each tab is built the first time it is shown and then kept, so unsaved edits survive switching tabs.</summary>
public sealed partial class SettingsPage : Page
{
    private readonly Dictionary<string, SettingsTab> _tabs = [];

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Show(SelectedKey());
    }

    // Every tab gets the same width so switching tabs doesn't make the cards jump.
    private void Scroller_SizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e) =>
        TabHost.Width = Math.Clamp(Scroller.ActualWidth - 16, 320, 900);

    private string SelectedKey() => Tabs.SelectedItem?.Tag as string ?? "appearance";

    private void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        // Selection is set while the XAML loads, before the host exists.
        if (TabHost is not null)
        {
            Show(SelectedKey());
        }
    }

    private void Show(string key)
    {
        StatusBar.IsOpen = false;
        if (!_tabs.TryGetValue(key, out var tab))
        {
            _tabs[key] = tab = Create(key);
        }

        TabHost.Content = tab;
    }

    private SettingsTab Create(string key) => key switch
    {
        "behavior" => new BehaviorTab(Status),
        "sessions" => new SessionsTab(Status),
        "registries" => new RegistriesTab(Status),
        "updates" => new UpdatesTab(Status),
        _ => new AppearanceTab(Status),
    };

    private void Status(string title, string message, InfoBarSeverity severity)
    {
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }
}
