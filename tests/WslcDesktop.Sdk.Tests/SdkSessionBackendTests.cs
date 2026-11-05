using WslcDesktop.Backends;
using WslcDesktop.Cli;
using WslcDesktop.Containers;
using WslcDesktop.Sdk;

namespace WslcDesktop.Sdk.Tests;

/// <summary>Runs the SDK adapter against real WSL. Opt in with WSLC_INTEGRATION=1; a throwaway session is created and terminated.</summary>
public sealed class SdkSessionBackendTests : IAsyncLifetime
{
    private static readonly bool Enabled = Environment.GetEnvironmentVariable("WSLC_INTEGRATION") == "1";

    private readonly string _name = "WslcDesktopTest" + Guid.NewGuid().ToString("N")[..8];
    private SdkSessionBackend? _backend;

    public Task InitializeAsync()
    {
        if (Enabled)
        {
            _backend = SdkSessionBackend.Create(_name, Path.Combine(Path.GetTempPath(), _name), new WslcCliBackend(WslcProcess.Default, _name));
        }

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_backend is not null)
        {
            _backend.Dispose();
            try
            {
                await new WslcCli(WslcProcess.Default).TerminateSessionAsync(_name);
            }
            catch
            {
                // Best effort: the session may already be gone.
            }
        }
    }

    [SkippableFact]
    public async Task A_container_goes_through_create_start_inspect_stop_delete()
    {
        Skip.IfNot(Enabled, "Set WSLC_INTEGRATION=1 to run against real WSL.");
        var backend = _backend!;

        await backend.PullImageAsync("docker.io/library/alpine:3", progress: null);
        await backend.CreateContainerAsync("itest", "docker.io/library/alpine:3", ["sh", "-c", "echo hello-itest; sleep 300"], new Dictionary<string, string> { ["GREETING"] = "hi" });
        await backend.StartContainerAsync("itest");

        var listed = await backend.ListContainersAsync();
        Assert.Contains(listed, c => c.Name == "itest" && c.State.Equals("running", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("itest", await backend.InspectContainerAsync("itest"));

        var logs = new List<string>();
        await backend.StreamLogsAsync("itest", new LogOptions(Tail: 10, Follow: false, Timestamps: false), logs.Add, CancellationToken.None);
        Assert.Contains("hello-itest", logs);

        var output = new List<string>();
        var seen = new TaskCompletionSource();
        using (var shell = backend.StartShell("itest", "/bin/sh", line =>
        {
            output.Add(line);
            if (line.Contains("shell-ok"))
            {
                seen.TrySetResult();
            }
        }, _ => { }))
        {
            await shell.SendLineAsync("echo shell-ok");
            await seen.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }

        Assert.Contains(output, l => l.Contains("shell-ok"));

        await backend.StopContainerAsync("itest");
        await backend.DeleteContainerAsync("itest");
        Assert.DoesNotContain(await backend.ListContainersAsync(), c => c.Name == "itest");

        await backend.DeleteImageAsync("docker.io/library/alpine:3");
    }
}
