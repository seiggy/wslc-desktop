using WslcDesktop.Views.Settings.Tabs.Cards;

namespace WslcDesktop.Views.Settings.Tabs;

/// <summary>Theme, accent colour and the terminal's font.</summary>
public sealed class AppearanceTab : SettingsTab
{
    public AppearanceTab(StatusReporter report) : base(report)
    {
        Children.Add(ThemeCard.Create(App.Host.Settings, report));
        Children.Add(TerminalCard.Create(App.Host.Settings, report));
    }
}
