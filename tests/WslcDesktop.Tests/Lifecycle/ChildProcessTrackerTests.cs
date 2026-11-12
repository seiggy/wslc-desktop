using System.Diagnostics;
using WslcDesktop.Lifecycle;

namespace WslcDesktop.Tests.Lifecycle;

public class ChildProcessTrackerTests
{
    private static string PowerShell => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
        "WindowsPowerShell", "v1.0", "powershell.exe");

    [Fact]
    public async Task Kill_ends_the_child_and_the_processes_it_started()
    {
        var pidFile = Path.Combine(Path.GetTempPath(), $"tracker-test-{Guid.NewGuid():N}.pid");
        var info = new ProcessStartInfo(PowerShell) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[]
        {
            "-NoLogo", "-NoProfile", "-NonInteractive", "-Command",
            "$p = Start-Process -FilePath powershell.exe -ArgumentList '-NoProfile','-Command','Start-Sleep -Seconds 60' " +
            "-WindowStyle Hidden -PassThru; " +
            "[IO.File]::WriteAllText($env:TRACKER_PID_FILE + '.tmp', [string]$p.Id); " +
            "[IO.File]::Move($env:TRACKER_PID_FILE + '.tmp', $env:TRACKER_PID_FILE); Start-Sleep -Seconds 60",
        })
        {
            info.ArgumentList.Add(arg);
        }

        info.Environment["TRACKER_PID_FILE"] = pidFile;

        using var child = ChildProcessTracker.Start(info);
        Process? grandchild = null;
        try
        {
            var waited = Stopwatch.StartNew();
            while (!File.Exists(pidFile))
            {
                Assert.False(child.HasExited, "The child exited before starting the grandchild.");
                Assert.True(waited.Elapsed < TimeSpan.FromSeconds(30), "The grandchild didn't start.");
                await Task.Delay(50);
            }

            grandchild = Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(pidFile)));
            Assert.False(grandchild.HasExited);

            ChildProcessTracker.Kill(child);

            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await grandchild.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
            }

            if (grandchild is { HasExited: false })
            {
                grandchild.Kill();
            }

            grandchild?.Dispose();
            File.Delete(pidFile);
            File.Delete(pidFile + ".tmp");
        }
    }

    [Fact]
    public async Task Killing_an_exited_child_is_a_no_op()
    {
        var info = new ProcessStartInfo(PowerShell) { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-Command");
        info.ArgumentList.Add("exit 0");

        using var child = ChildProcessTracker.Start(info);
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));

        ChildProcessTracker.Kill(child);

        Assert.Equal(0, child.ExitCode);
    }
}
