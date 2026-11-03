using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Velopack;
using WslcDesktop.Platform;
using WslcDesktop.Services;

namespace WslcDesktop;

// Custom entry point (DISABLE_XAML_GENERATED_MAIN) so a second launch can redirect before any window exists.
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Must run first: handles install/update/uninstall hooks and may exit the process before any UI exists.
        VelopackApp.Build().Run();

        WinRT.ComWrappersSupport.InitializeComWrappers();
        var background = StartupPolicy.IsBackgroundLaunch(args) || SingleInstance.IsStartupTaskActivation();

        // A login launch must never raise the window of an instance that is already running.
        if (SingleInstance.RedirectIfSecondary(quiet: background))
        {
            return 0;
        }

        App.BackgroundLaunch = background;
        Application.Start(p =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App();
        });
        return 0;
    }
}
