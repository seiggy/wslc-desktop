using System.Collections.Concurrent;
using System.Diagnostics;

namespace WslcDesktop.Services;

/// <summary>Waits for work that may block in native code without letting it hold up the caller.</summary>
public static class BoundedWait
{
    /// <summary>Returns true if <paramref name="work"/> finished within <paramref name="timeout"/>; failures count as finished.</summary>
    public static bool Run(Func<Task> work, TimeSpan timeout)
    {
        var task = Task.Run(work);
        try
        {
            return task.Wait(timeout);
        }
        catch (AggregateException)
        {
            return true;
        }
    }
}

/// <summary>Tracks wslc.exe children so closing the app never leaves them behind.</summary>
public static class ChildProcessTracker
{
    private static readonly ConcurrentDictionary<Process, byte> Children = new();

    public static int Count => Children.Count;

    public static Process Start(ProcessStartInfo info)
    {
        var process = Process.Start(info) ?? throw new InvalidOperationException("Couldn't start wslc.exe.");
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
