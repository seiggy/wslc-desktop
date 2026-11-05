using WslcDesktop.Views.Settings.Tabs.Cards;

namespace WslcDesktop.Views.Settings.Tabs;

/// <summary>The resources of the app's own session, and wslc's settings.yaml.</summary>
public sealed class SessionsTab : SettingsTab
{
    public SessionsTab(StatusReporter report) : base(report)
    {
        Children.Add(AppSessionCard.Create(App.Host, report));
        Children.Add(WslcConfigCard.Create(report));
    }
}
