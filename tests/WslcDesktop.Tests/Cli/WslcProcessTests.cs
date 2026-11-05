using System.Diagnostics;
using WslcDesktop.Cli;

namespace WslcDesktop.Tests.Cli;

public class WslcProcessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_still_applies_when_the_child_does_not_read_stdin(bool callerCancels)
    {
        var pidFile = Path.Combine(Path.GetTempPath(), $"wslc-process-test-{Guid.NewGuid():N}.pid");
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        IWslcProcess process = new WslcProcess(executable);
        using var cancellation = new CancellationTokenSource();
        if (callerCancels)
        {
            cancellation.CancelAfter(TimeSpan.FromSeconds(2));
        }

        var invocation = new WslcInvocation(
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command",
             "[IO.File]::WriteAllText($env:WSLC_TEST_PID_FILE, [string]$PID); Start-Sleep -Seconds 60"],
            new Dictionary<string, string> { ["WSLC_TEST_PID_FILE"] = pidFile },
            StandardInput: new string('x', 8 * 1024 * 1024),
            Timeout: callerCancels ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(2));
        var run = process.RunAsync(invocation, cancellation.Token);
        try
        {
            if (callerCancels)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(10)));
            }
            else
            {
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() => run.WaitAsync(TimeSpan.FromSeconds(10)));
                Assert.Equal("wslc didn't respond in time.", error.Message);
            }

            Assert.True(File.Exists(pidFile), "The test-owned child must start before the timeout.");
            var pid = int.Parse(await File.ReadAllTextAsync(pidFile));
            Assert.Throws<ArgumentException>(() => Process.GetProcessById(pid));
        }
        finally
        {
            if (File.Exists(pidFile))
            {
                var pid = int.Parse(await File.ReadAllTextAsync(pidFile));
                try
                {
                    using var child = Process.GetProcessById(pid);
                    child.Kill(entireProcessTree: true);
                    await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch (ArgumentException)
                {
                    // The child has already exited.
                }

                File.Delete(pidFile);
            }

            try
            {
                await run.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception) when (run.IsCompleted)
            {
                // Observe the run's exception after cleanup; its outcome is asserted above.
            }
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
