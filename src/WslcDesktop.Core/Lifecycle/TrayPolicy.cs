using WslcDesktop.Settings;

namespace WslcDesktop.Lifecycle;

/// <summary>Pure decisions for the tray feature, kept free of UI types so they can be unit tested.</summary>
public static class TrayPolicy
{
    public static readonly int[] IconSizes = [16, 20, 24, 32];

    public static bool ShowTrayIcon(AppSettings s) => s.MinimizeToTray || s.CloseMinimizesToTray;

    public static bool HideOnMinimize(AppSettings s) => s.MinimizeToTray;

    // Closing really closes unless the user asked for tray behavior, or Exit was chosen explicitly.
    public static bool InterceptClose(AppSettings s, bool exitRequested) => !exitRequested && s.CloseMinimizesToTray;

    // Smallest bundled size that covers the requested pixel size, else the largest.
    public static int PickIconSize(int requestedPixels) =>
        IconSizes.FirstOrDefault(s => s >= requestedPixels, IconSizes[^1]);

    // The "dark" assets carry a light glyph meant for a dark taskbar.
    public static string IconFileName(bool taskbarIsLight, int size) =>
        $"tray-{(taskbarIsLight ? "light" : "dark")}-{PickIconSize(size)}.png";

    public static string Tooltip(int? running, int? total) => running switch
    {
        null => "WSLc Desktop Manager",
        0 => "WSLc Desktop Manager — no containers running",
        1 => "WSLc Desktop Manager — 1 container running",
        _ => $"WSLc Desktop Manager — {running} containers running",
    };

    public static string StatusLine(int? running, int? total) => running is null
        ? "Status unavailable"
        : $"{running} of {total ?? running} containers running";

    // Hidden windows poll slowly: the tray only needs a running count.
    public static TimeSpan PollInterval(bool windowHidden, TimeSpan visible) =>
        windowHidden ? TimeSpan.FromSeconds(10) : visible;
}
