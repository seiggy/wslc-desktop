using WslcDesktop.Lifecycle;
using WslcDesktop.Settings;

namespace WslcDesktop.Lifecycle.Startup;

/// <summary>Pure decisions for Start with Windows and background launches, kept free of OS calls so they can be tested.</summary>
public static class StartupPolicy
{
    public const string BackgroundArgument = "--background";

    public const string RunValueName = "WslcDesktopManager";

    public const string StartupTaskId = "WslcDesktopStartup";

    public static bool IsBackgroundLaunch(IEnumerable<string> args) =>
        args.Any(a => a.Equals(BackgroundArgument, StringComparison.OrdinalIgnoreCase)
            || a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The command stored in HKCU Run. Velopack installs use the launcher stub in the install root, which survives
    /// updates (the versioned "current" folder does not); other forms use the running exe.
    /// </summary>
    public static string RunCommand(InstallForm form, string processPath, string? velopackStubPath) =>
        Quote(form == InstallForm.Velopack && !string.IsNullOrEmpty(velopackStubPath) ? velopackStubPath : processPath)
        + " " + BackgroundArgument;

    // Windows keeps Task Manager's Startup toggle in StartupApproved: first byte 02/06 = enabled, 03/07 = disabled.
    public static bool IsApprovedByUser(byte[]? startupApproved) =>
        startupApproved is not { Length: > 0 } || (startupApproved[0] & 1) == 0;

    /// <summary>Do both registry commands launch the same exe? Tolerates quoting and the trailing argument.</summary>
    public static bool SameExecutable(string? a, string? b) =>
        string.Equals(Executable(a), Executable(b), StringComparison.OrdinalIgnoreCase);

    public static string Executable(string? command)
    {
        var c = (command ?? string.Empty).Trim();
        if (c.StartsWith('"'))
        {
            var end = c.IndexOf('"', 1);
            return end > 0 ? c[1..end] : c[1..];
        }

        var exe = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? c[..(exe + 4)] : c;
    }

    /// <summary>
    /// A background launch hides to the tray when the tray is enabled. Without a tray the window would be
    /// unreachable, so it starts minimized to the taskbar instead.
    /// </summary>
    public static BackgroundStart Resolve(bool backgroundLaunch, AppSettings settings) =>
        !backgroundLaunch ? BackgroundStart.Normal
        : TrayPolicy.ShowTrayIcon(settings) ? BackgroundStart.HiddenInTray
        : BackgroundStart.MinimizedToTaskbar;

    private static string Quote(string path) => path.Contains(' ') || path.StartsWith('"') ? $"\"{path.Trim('"')}\"" : path;
}
