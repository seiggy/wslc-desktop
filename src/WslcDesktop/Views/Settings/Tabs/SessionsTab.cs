using WslcDesktop.Views.Settings.Tabs.Cards;

namespace WslcDesktop.Views.Settings.Tabs;

/// <summary>wslc's settings.yaml, which configures the default session the app shares with the wslc CLI.</summary>
public sealed class SessionsTab : SettingsTab
{
    public SessionsTab(StatusReporter report) : base(report)
    {
        // AppSessionCard is hidden: the app joins the CLI's default session, so its CPU, memory and GPU settings had no
        // effect. Session resources come from settings.yaml until session management moves to the Environment page.
        Children.Add(WslcConfigCard.Create(report));
    }
}
