using Microsoft.UI.Xaml;
using WslcDesktop.Services;

namespace WslcDesktop;

public partial class App : Application
{
    public static WslcHost Host { get; } = new();

    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Closed += (_, _) => Host.Shutdown();
        _window.Activate();
    }
}
