using WslcDesktop.Localization;
namespace WslcDesktop.Lifecycle.Startup;

/// <summary>The explanation shown under the Start with Windows switch.</summary>
public static class StartupHelp
{
    private static string Explanation =>
        UiStrings.Get("StartsWSLcDesktopManagerWhenYouSignInTo");

    private static string TrayTip => UiStrings.Get("TurnOnMinimizeToTrayToKeepItOut");

    public static string Describe(StartupStatus status, bool trayIconShown)
    {
        if (status.Detail.Length > 0)
        {
            return status.Detail;
        }

        return status.IsOn && !trayIconShown ? Explanation + TrayTip : Explanation;
    }
}
