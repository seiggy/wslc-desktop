using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WslcDesktop.Services;

namespace WslcDesktop.Platform;

/// <summary>
/// Connects the main window to the tray icon: hide on minimize/close per the user's settings, restore on tray
/// click or second launch, and a real Exit. Settings changes apply immediately via <see cref="Apply"/>.
/// </summary>
internal sealed class TrayController
{
    private readonly Window _window;
    private readonly WslcHost _host;
    private TrayIcon? _tray;
    private bool _exiting;

    public TrayController(Window window, WslcHost host)
    {
        _window = window;
        _host = host;
        _window.AppWindow.Changed += OnChanged;
        _window.AppWindow.Closing += OnClosing;
        _host.Monitor.Updated += OnMonitorUpdated;
        Apply();
    }

    public bool IsHidden => !_window.AppWindow.IsVisible;

    public void Apply()
    {
        var s = _host.Settings;
        if (TrayPolicy.ShowTrayIcon(s))
        {
            _tray ??= CreateTray();
            _tray.Show();
            UpdateTooltip();
        }
        else
        {
            _tray?.Dispose();
            _tray = null;
        }
    }

    public void Restore()
    {
        var aw = _window.AppWindow;
        if (!aw.IsVisible)
        {
            aw.Show();
        }

        if (aw.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } p)
        {
            p.Restore();
        }

        _window.Activate();
        Foreground(_window);
        SetBackground(false);
    }

    public void Exit()
    {
        _exiting = true;
        _window.Close();
    }

    public void Dispose()
    {
        _host.Monitor.Updated -= OnMonitorUpdated;
        _tray?.Dispose();
        _tray = null;
    }

    private TrayIcon CreateTray()
    {
        var tray = new TrayIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Tray"))
        {
            StatusProvider = StatusLine,
        };
        tray.OpenRequested += () => _window.DispatcherQueue.TryEnqueue(Restore);
        tray.ExitRequested += () => _window.DispatcherQueue.TryEnqueue(Exit);
        return tray;
    }

    private void OnChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPresenterChange
            && sender.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized }
            && TrayPolicy.HideOnMinimize(_host.Settings)
            && _tray is not null)
        {
            HideToTray();
        }
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_tray is not null && TrayPolicy.InterceptClose(_host.Settings, _exiting))
        {
            args.Cancel = true;
            HideToTray();
        }
    }

    private void HideToTray()
    {
        _window.AppWindow.Hide();
        SetBackground(true);
    }

    private void SetBackground(bool hidden)
    {
        _host.Telemetry.Paused = hidden;
        _host.Monitor.SetPeriod(TrayPolicy.PollInterval(hidden, ContainerMonitor.Interval));
    }

    private void OnMonitorUpdated() => _window.DispatcherQueue.TryEnqueue(UpdateTooltip);

    private void UpdateTooltip()
    {
        if (_tray is null)
        {
            return;
        }

        _tray.SetTooltip(TrayPolicy.Tooltip(RunningCount(), _host.Monitor.Containers.Count));
    }

    private int? RunningCount() => _host.Monitor.HasLoaded
        ? _host.Monitor.Containers.Count(c => ContainerItem.IsRunningState(c.State))
        : null;

    private string StatusLine() => TrayPolicy.StatusLine(RunningCount(), _host.Monitor.Containers.Count);

    private static void Foreground(Window window)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        if (SetForegroundWindow(hwnd))
        {
            return;
        }

        // Foreground lock: borrow the foreground thread's input queue for the call.
        var fg = GetForegroundWindow();
        var fgThread = GetWindowThreadProcessId(fg, out _);
        var me = GetCurrentThreadId();
        if (fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true))
        {
            SetForegroundWindow(hwnd);
            AttachThreadInput(me, fgThread, false);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint from, uint to, bool attach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
