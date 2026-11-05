using System.Collections.Concurrent;
using System.Diagnostics;

namespace WslcDesktop.Lifecycle;

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
