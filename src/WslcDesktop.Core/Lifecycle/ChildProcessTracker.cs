using WslcDesktop.Localization;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace WslcDesktop.Lifecycle;

/// <summary>
/// Tracks wslc.exe children so closing the app never leaves them behind. Each child runs in its own job object, so
/// killing one is a single <c>TerminateJobObject</c> call instead of a snapshot of every process and thread on the
/// machine, and the OS ends the job if this app dies without cleaning up.
/// </summary>
public static class ChildProcessTracker
{
    private static readonly ConcurrentDictionary<Process, ProcessJob?> Children = new();

    public static int Count => Children.Count;

    public static Process Start(ProcessStartInfo info)
    {
        var process = Process.Start(info) ?? throw new InvalidOperationException(UiStrings.Get("CouldnTStartWslcExe"));
        Track(process);
        return process;
    }

    public static void Track(Process process)
    {
        ProcessJob? job;
        try
        {
            job = ProcessJob.TryAssign(process);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already exited; the checks below untrack it.
            job = null;
        }

        Children[process] = job;
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => Release(process);
        if (process.HasExited)
        {
            Release(process);
        }
    }

    public static void Kill(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }

            if (Children.TryGetValue(process, out var job) && job is not null && job.Terminate())
            {
                return;
            }

            process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException)
        {
            // Already exited or disposed; a tree member may also exit while the tree is being killed.
        }
    }

    public static void KillAll()
    {
        foreach (var process in Children.Keys)
        {
            Kill(process);
            Release(process);
        }
    }

    private static void Release(Process process)
    {
        if (Children.TryRemove(process, out var job))
        {
            job?.Dispose();
        }
    }
}
