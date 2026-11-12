using WslcDesktop.Localization;
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
        null => UiStrings.Get("AppTitle"),
        0 => UiStrings.Get("WSLcDesktopManagerNoContainersRunning"),
        1 => UiStrings.Get("WSLcDesktopManagerContainerRunning"),
        _ => UiStrings.Format("WSLcDesktopManagerContainersRunning", running),
    };

    public static string StatusLine(int? running, int? total) => running is null
        ? UiStrings.Get("StatusUnavailable")
        : UiStrings.Format("OfContainersRunning", running, total ?? running);
}
