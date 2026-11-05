using WslcDesktop.Backends;
using WslcDesktop.Containers;
using WslcDesktop.Containers.Creation;

namespace WslcDesktop.Tests.Backends;

public class WslcCliBackendContainerTests
{
    private readonly FakeWslcProcess _process = new();

    private WslcCliBackend Backend(string session = "lab") => new(_process, session);

    [Fact]
    public async Task Start_runs_start_with_the_container_name()
    {
        await Backend().StartContainerAsync("web");
        Assert.Equal(["--session", "lab", "start", "web"], _process.LastArgs);
    }

    [Fact]
    public async Task Stop_runs_stop_with_the_container_name()
    {
        await Backend().StopContainerAsync("web");
        Assert.Equal(["--session", "lab", "stop", "web"], _process.LastArgs);
    }

    [Fact]
    public async Task Delete_force_removes_the_container()
    {
        await Backend().DeleteContainerAsync("web");
        Assert.Equal(["--session", "lab", "rm", "--force", "web"], _process.LastArgs);
    }

    [Fact]
    public async Task Inspect_returns_the_json_from_wslc()
    {
        _process.Returns("[{\"Name\":\"web\"}]");

        var json = await Backend().InspectContainerAsync("web");

        Assert.Equal(["--session", "lab", "inspect", "web"], _process.LastArgs);
        Assert.Equal("[{\"Name\":\"web\"}]", json);
    }

    [Fact]
    public async Task Listing_containers_parses_json_lines()
    {
        _process.Returns("""
            {"Names":"web","Image":"nginx","ID":"abc","State":"running","Command":"\"nginx -g\"","Ports":"80/tcp","Labels":"a=b","Status":"Up 2 minutes"}
            """);

        var rows = await Backend().ListContainersAsync();

        Assert.Equal(["--session", "lab", "list", "--all", "--no-trunc", "--format", "json"], _process.LastArgs);
        var row = Assert.Single(rows);
        Assert.Equal("web", row.Name);
        Assert.Equal("running", row.State);
        Assert.Equal("nginx -g", row.Command);
    }

    [Fact]
    public async Task Creating_a_container_builds_flags_in_order()
    {
        var spec = new NewContainerSpec(
            "web", "nginx:latest", ["nginx", "-g"], ["8080:80"], ["data:/d"], ["A=1"], "backnet");

        await Backend().CreateContainerAsync(spec);

        Assert.Equal(
            ["--session", "lab", "create", "--name", "web", "--publish", "8080:80", "--volume", "data:/d",
             "--env", "A=1", "--network", "backnet", "nginx:latest", "nginx", "-g"],
            _process.LastArgs);
    }

    [Fact]
    public async Task Streaming_logs_builds_flags_in_order_and_forwards_each_line()
    {
        _process.StreamsLines("one", "two");
        var lines = new List<string>();

        await Backend().StreamLogsAsync("web", new LogOptions(Tail: 100, Follow: true, Timestamps: true), lines.Add, CancellationToken.None);

        Assert.Equal(["--session", "lab", "logs", "--tail", "100", "--follow", "--timestamps", "web"], _process.LastArgs);
        Assert.Equal(["one", "two"], lines);
    }

    [Fact]
    public async Task Streaming_logs_without_follow_or_timestamps_omits_those_flags()
    {
        await Backend().StreamLogsAsync("web", new LogOptions(Tail: 5, Follow: false, Timestamps: false), _ => { }, CancellationToken.None);

        Assert.Equal(["--session", "lab", "logs", "--tail", "5", "web"], _process.LastArgs);
    }

    [Fact]
    public void Starting_a_shell_runs_an_interactive_exec_and_forwards_output_and_exit()
    {
        var output = new List<string>();
        var exits = new List<int>();

        var shell = Backend().StartShell("web", "/bin/sh", output.Add, exits.Add);
        _process.Shell.Emit("hello");
        _process.Shell.Exit(3);

        Assert.Equal(["--session", "lab", "exec", "--interactive", "web", "/bin/sh"], _process.LastArgs);
        Assert.Same(_process.Shell, shell);
        Assert.Equal(["hello"], output);
        Assert.Equal([3], exits);
    }

    [Fact]
    public async Task Creating_with_env_passes_values_in_the_process_environment_not_the_command_line()
    {
        var env = new Dictionary<string, string> { ["TOKEN"] = "s3cret" };

        await Backend().CreateContainerAsync("job", "alpine", ["sh"], env);

        Assert.Equal(["--session", "lab", "create", "--name", "job", "--env", "TOKEN", "alpine", "sh"], _process.LastArgs);
        Assert.Equal("s3cret", _process.Calls[^1].Environment!["TOKEN"]);
        Assert.DoesNotContain("s3cret", _process.LastArgs);
    }
}
