using System.Diagnostics;
using WslcDesktop.Cli;

namespace WslcDesktop.Tests.Cli;

[Collection(ChildProcessCollection.Name)]
public class WslcProcessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_still_applies_when_the_child_does_not_read_stdin(bool callerCancels)
    {
        var pidFile = Path.Combine(Path.GetTempPath(), $"wslc-process-test-{Guid.NewGuid():N}.pid");
        var readyFile = pidFile + ".ready";
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        IWslcProcess process = new WslcProcess(executable);
        using var cancellation = new CancellationTokenSource();

        // Delay readiness past the old cancellation deadline to reproduce a slow-starting child.
        var invocation = new WslcInvocation(
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command",
             "[IO.File]::WriteAllText($env:WSLC_TEST_PID_FILE + '.tmp', [string]$PID); " +
             "[IO.File]::Move($env:WSLC_TEST_PID_FILE + '.tmp', $env:WSLC_TEST_PID_FILE); " +
             "Start-Sleep -Seconds 3; [IO.File]::WriteAllText($env:WSLC_TEST_PID_FILE + '.ready', 'ready'); " +
             "Start-Sleep -Seconds 60"],
            new Dictionary<string, string> { ["WSLC_TEST_PID_FILE"] = pidFile },
            StandardInput: new string('x', 8 * 1024 * 1024),
            Timeout: callerCancels ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(15));
        var run = process.RunAsync(invocation, cancellation.Token);
        var readiness = Stopwatch.StartNew();
        Process? child = null;

        async Task WaitForFileAsync(string path)
        {
            while (!File.Exists(path))
            {
                Assert.False(run.IsCompleted, "The child exited before signaling readiness.");
                Assert.True(readiness.Elapsed < TimeSpan.FromSeconds(10), "The child did not signal readiness within 10 seconds.");
                await Task.Delay(50);
            }
        }

        try
        {
            await WaitForFileAsync(pidFile);
            child = Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(pidFile)));
            await WaitForFileAsync(readyFile);
            Assert.False(child.HasExited, "The child must still be running before cancellation.");

            if (callerCancels)
            {
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(10)));
            }
            else
            {
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() => run.WaitAsync(TimeSpan.FromSeconds(20)));
                Assert.Equal("wslc didn't respond in time.", error.Message);
            }

            Assert.True(child.HasExited, "Cancellation must terminate the child process.");
        }
        finally
        {
            cancellation.Cancel();
            if (child is not null)
            {
                try
                {
                    if (!child.HasExited)
                    {
                        child.Kill(entireProcessTree: true);
                    }

                    await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch (InvalidOperationException) when (child.HasExited)
                {
                    // The child has already exited.
                }
                finally
                {
                    child.Dispose();
                }
            }

            try
            {
                await run.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception) when (run.IsCompleted)
            {
                // Observe the run's exception after cleanup; its outcome is asserted above.
            }

            File.Delete(pidFile);
            File.Delete(pidFile + ".tmp");
            File.Delete(readyFile);
        }
    }

    [Fact]
    public async Task Output_is_drained_while_stdin_is_written_and_closed()
    {
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        IWslcProcess process = new WslcProcess(executable);
        var invocation = new WslcInvocation(
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command",
             "[Console]::Out.Write('o' * 131072); [Console]::Error.Write('e' * 131072); " +
             "$inputText = [Console]::In.ReadToEnd(); [Console]::Out.Write($inputText.Length)"],
            StandardInput: new string('x', 524288),
            Timeout: TimeSpan.FromSeconds(10));

        var output = await process.RunAsync(invocation).WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(new string('o', 131072) + "524288", output);
    }
}
