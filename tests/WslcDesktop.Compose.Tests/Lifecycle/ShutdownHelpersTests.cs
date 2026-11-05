using System.Diagnostics;
using WslcDesktop.Lifecycle;

namespace WslcDesktop.Compose.Tests.Lifecycle;

public class ShutdownHelpersTests
{
    [Fact]
    public void BoundedWait_returns_true_when_work_finishes()
    {
        Assert.True(BoundedWait.Run(() => Task.CompletedTask, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void BoundedWait_gives_up_on_blocked_work()
    {
        using var release = new ManualResetEventSlim();
        var sw = Stopwatch.StartNew();
        var finished = BoundedWait.Run(() => Task.Run(() => release.Wait()), TimeSpan.FromMilliseconds(200));
        release.Set();
        Assert.False(finished);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void BoundedWait_treats_failures_as_finished()
    {
        Assert.True(BoundedWait.Run(() => Task.FromException(new InvalidOperationException()), TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task ChildProcessTracker_kills_tracked_processes()
    {
        var info = new ProcessStartInfo("powershell", "-NoProfile -Command Start-Sleep 60")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        var process = ChildProcessTracker.Start(info);
        Assert.False(process.HasExited);

        ChildProcessTracker.KillAll();

        Assert.True(await Task.Run(() => process.WaitForExit(10000)));
        Assert.Equal(0, ChildProcessTracker.Count);
    }
}
