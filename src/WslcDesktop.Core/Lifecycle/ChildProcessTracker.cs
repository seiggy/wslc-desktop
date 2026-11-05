using WslcDesktop.Localization;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace WslcDesktop.Lifecycle;

/// <summary>Tracks wslc.exe children so closing the app never leaves them behind.</summary>
public static class ChildProcessTracker
{
    private static readonly ConcurrentDictionary<Process, byte> Children = new();

    public static int Count => Children.Count;

    public static Process Start(ProcessStartInfo info)
    {
        var process = Process.Start(info) ?? throw new InvalidOperationException(UiStrings.Get("CouldnTStartWslcExe"));
        Track(process);
        return process;
    }

    public static void Track(Process process)
    {
        Children[process] = 0;
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => Children.TryRemove(process, out _);
        if (process.HasExited)
        {
            Children.TryRemove(process, out _);
        }
    }

    public static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already exited or disposed.
        }
    }

    public static void KillAll()
    {
        foreach (var process in Children.Keys)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Already exited.
            }
        }

        Children.Clear();
    }
}
