using Microsoft.UI;
using Microsoft.UI.Xaml;
using Windows.UI;
using Windows.UI.ViewManagement;
using WslcDesktop.Settings;

namespace WslcDesktop.Services;

internal sealed class AppearanceService : IDisposable
{
    private static readonly string[] AccentKeys =
    [
        "SystemAccentColor", "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3",
        "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3",
    ];

    private static readonly UIColorType[] SystemColors =
    [
        UIColorType.Accent, UIColorType.AccentDark1, UIColorType.AccentDark2, UIColorType.AccentDark3,
        UIColorType.AccentLight1, UIColorType.AccentLight2, UIColorType.AccentLight3,
    ];

    private readonly Window _window;
    private readonly FrameworkElement _root;
    private readonly UISettings _system = new();
    private readonly AccessibilitySettings _accessibility = new();
    private bool _disposed;

    public AppearanceService(Window window)
    {
        _window = window;
        _root = (FrameworkElement)window.Content;
        _root.ActualThemeChanged += ActualThemeChanged;
        _system.ColorValuesChanged += SystemColorsChanged;
        Apply();
    }

    public void Apply()
    {
        var settings = App.Host.Settings;
        var palette = settings.AccentColor is { } hex ? AccentPalette.Create(AccentPalette.Parse(hex)) : null;
        for (var i = 0; i < AccentKeys.Length; i++)
        {
            Application.Current.Resources[AccentKeys[i]] = palette is null
                ? _system.GetColorValue(SystemColors[i])
                : ToColor(palette[i]);
        }

        // Re-evaluate ThemeResource references after changing the accent, without recreating pages.
        _root.RequestedTheme = _root.ActualTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        _root.RequestedTheme = settings.Theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        UpdateTitleBar();
    }

    private static Color ToColor(uint rgb) => Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private void ActualThemeChanged(FrameworkElement sender, object args) => UpdateTitleBar();

    private void UpdateTitleBar()
    {
        var bar = _window.AppWindow.TitleBar;
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = _accessibility.HighContrast
            ? _system.GetColorValue(UIColorType.Foreground)
            : _root.ActualTheme == ElementTheme.Dark ? Colors.White : Colors.Black;
        bar.ButtonInactiveForegroundColor = bar.ButtonForegroundColor;
    }

    private void SystemColorsChanged(UISettings sender, object args) => QueueApply();

    private void QueueApply() => _window.DispatcherQueue.TryEnqueue(() =>
    {
        if (!_disposed)
        {
            Apply();
        }
    });

    public void Dispose()
    {
        _disposed = true;
        _root.ActualThemeChanged -= ActualThemeChanged;
        _system.ColorValuesChanged -= SystemColorsChanged;
    }
}
