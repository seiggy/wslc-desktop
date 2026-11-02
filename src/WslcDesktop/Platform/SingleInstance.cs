using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Windows.AppLifecycle;

namespace WslcDesktop.Platform;

/// <summary>
/// Ensures one running instance. Uses AppInstance key registration + activation redirection, which behaves the
/// same for unpackaged and MSIX runs. Registrations die with the process, so a crashed instance never blocks a
/// new launch; a hung one is handled by the redirect timeout and the window fallback.
/// </summary>
internal static class SingleInstance
{
    private const string Key = "WslcDesktop.MainInstance";
    private static readonly TimeSpan RedirectTimeout = TimeSpan.FromSeconds(5);

    // Raised (on a background thread) in the first instance when another launch was redirected to it.
    public static event Action? ActivationRequested;

    /// <summary>Returns true when another instance owns the app and this process should exit.</summary>
    public static bool RedirectIfSecondary()
    {
        try
        {
            var main = AppInstance.FindOrRegisterForKey(Key);
            if (main.IsCurrent)
            {
                main.Activated += (_, _) => ActivationRequested?.Invoke();
                return false;
            }

            AllowSetForegroundWindow(-1);
            var args = AppInstance.GetCurrent().GetActivatedEventArgs();

            // Redirecting from the STA main thread can deadlock, so wait on a pool thread.
            var redirect = Task.Run(async () => await main.RedirectActivationToAsync(args));
            if (redirect.Wait(RedirectTimeout))
            {
                return true;
            }
        }
        catch (Exception)
        {
            // Fall through to the window-based fallback below.
        }

        return ActivateExistingOrWarn();
    }

    private static bool ActivateExistingOrWarn()
    {
        var self = Environment.ProcessId;
        var others = Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName)
            .Where(p => p.Id != self)
            .ToList();
        if (others.Count == 0)
        {
            // Nothing else is running, so a leftover registration is stale: start normally.
            return false;
        }

        var ids = others.Select(p => (uint)p.Id).ToHashSet();
        nint found = 0;
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out var pid);
            if (ids.Contains(pid) && GetWindow(h, 4 /* GW_OWNER */) == 0)
            {
                var name = new System.Text.StringBuilder(64);
                GetClassName(h, name, name.Capacity);
                if (name.ToString() == "WinUIDesktopWin32WindowClass")
                {
                    found = h;
                    return false;
                }
            }

            return true;
        }, 0);

        if (found != 0)
        {
            ShowWindow(found, 9 /* SW_RESTORE */);
            ShowWindow(found, 5 /* SW_SHOW */);
            SetForegroundWindow(found);
            return true;
        }

        MessageBox(
            0,
            $"WSLc Desktop is already running (process {others[0].Id}) but isn't responding.\n\n"
                + "Close it from Task Manager, then open WSLc Desktop again.",
            "WSLc Desktop",
            0x30 /* MB_ICONWARNING */);
        return true;
    }

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc proc, nint lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint hwnd, uint cmd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hwnd, System.Text.StringBuilder name, int max);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hwnd, int cmd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(nint hwnd, string text, string caption, uint type);
}
