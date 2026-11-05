namespace WslcDesktop.Lifecycle.Startup;

/// <summary>The explanation shown under the Start with Windows switch.</summary>
public static class StartupHelp
{
    private const string Explanation =
        "Starts WSLc Desktop Manager when you sign in to Windows, minimized so it stays out of the way. "
        + "Restart policies only work while the app is running, so this is the way to keep them in effect.";

    private const string TrayTip = " Turn on \"Minimize to tray\" to keep it out of the taskbar too.";

    public static string Describe(StartupStatus status, bool trayIconShown)
    {
        if (status.Detail.Length > 0)
        {
            return status.Detail;
        }

        return status.IsOn && !trayIconShown ? Explanation + TrayTip : Explanation;
    }
}
