using Microsoft.UI.Xaml;
using WslcDesktop.Services;

namespace WslcDesktop;

public partial class App : Application
{
    public static WslcHost Host { get; } = new();

    private Window? _window;

    public static nint WindowHandle { get; private set; }

    internal static Platform.TrayController? Tray { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        WindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        Tray = new Platform.TrayController(_window, Host);
        _window.Closed += (_, _) =>
        {
            Tray.Dispose();
            Host.Shutdown();
        };
        Platform.SingleInstance.ActivationRequested += () => _window.DispatcherQueue.TryEnqueue(Tray.Restore);
        _window.Activate();
    }
}
