using Microsoft.UI.Xaml;
using WslcDesktop.Services;
using WslcDesktop.Lifecycle.Startup;

namespace WslcDesktop;

public partial class App : Application
{
    public static WslcHost Host { get; } = new();

    public static UpdateService Updates { get; } = new();

    private Window? _window;

    public static nint WindowHandle { get; private set; }

    /// <summary>Launched at sign-in (or with --background): start without taking over the screen.</summary>
    public static bool BackgroundLaunch { get; set; }

    internal static Platform.TrayController? Tray { get; private set; }

    internal static AppearanceService? Appearance { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        Appearance = new AppearanceService(_window);
        WindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        Tray = new Platform.TrayController(_window, Host);
        _window.Closed += (_, _) =>
        {
            Tray.Dispose();
            Appearance.Dispose();
            Host.Shutdown();
        };
        Platform.SingleInstance.ActivationRequested += () => _window.DispatcherQueue.TryEnqueue(Tray.Restore);
        Platform.StartupRegistration.RepairIfStale();
        switch (StartupPolicy.Resolve(BackgroundLaunch, Host.Settings))
        {
            case BackgroundStart.HiddenInTray:
                Tray.StartHidden();
                break;
            case BackgroundStart.MinimizedToTaskbar:
                Tray.StartMinimized();
                break;
            default:
                _window.Activate();
                break;
        }
    }
}
