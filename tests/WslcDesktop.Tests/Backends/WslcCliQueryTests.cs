using WslcDesktop.Cli;

namespace WslcDesktop.Tests.Backends;

public class WslcCliQueryTests
{
    private const string SessionTable =
        "ID CreatorPID Name\n1 100 wslc-cli-admin\n2 200 mine\n";

    [Fact]
    public async Task ListSessions_parses_the_table_skipping_the_header()
    {
        var cli = new WslcCli(new FakeWslcProcess().Returns(SessionTable));

        var rows = await cli.ListSessionsAsync();

        Assert.Equal(["wslc-cli-admin", "mine"], rows.Select(r => r.Name));
        Assert.Equal("200", rows[1].CreatorPid);
    }

    [Fact]
    public async Task TerminateSession_targets_the_named_session()
    {
        var process = new FakeWslcProcess();

        await new WslcCli(process).TerminateSessionAsync("mine");

        Assert.Equal(["--session", "mine", "system", "session", "terminate"], process.LastArgs);
    }

    [Fact]
    public async Task ListContainers_asks_for_all_containers_in_the_session()
    {
        var process = new FakeWslcProcess().Returns("{\"Names\":\"web\",\"State\":\"running\"}\n");

        var rows = await new WslcCli(process).ListContainersAsync("mine");

        Assert.Equal("web", rows.Single().Name);
        Assert.Equal(["--session", "mine", "list", "--all", "--no-trunc", "--format", "json"], process.LastArgs);
    }

    [Fact]
    public async Task GetStats_parses_usage_per_container()
    {
        var process = new FakeWslcProcess().Returns(
            "{\"Name\":\"web\",\"CPUPerc\":\"12.5%\",\"MemPerc\":\"1%\",\"MemUsage\":\"1MiB / 2MiB\",\"NetIO\":\"1kB / 2kB\",\"BlockIO\":\"0B / 0B\",\"PIDs\":3}\n");

        var stats = await new WslcCli(process).GetStatsAsync("mine");

        var sample = stats["web"];
        Assert.Equal(12.5, sample.CpuPercent);
        Assert.Equal(1024UL * 1024, sample.MemoryBytes);
        Assert.Equal(1000UL, sample.NetRxBytes);
    }

    [Fact]
    public async Task InspectImage_redacts_secrets_before_returning_the_raw_json()
    {
        var process = new FakeWslcProcess().Returns("[{\"Id\":\"abc\",\"Config\":{\"Env\":[\"TOKEN=hunter2\"]}}]");
        var cli = new WslcCli(process, text => text.Replace("hunter2", "***"));

        var detail = await cli.InspectImageAsync("mine", "img");

        Assert.DoesNotContain("hunter2", detail.RawJson);
        Assert.Equal(["--session", "mine", "image", "inspect", "img"], process.LastArgs);
    }
}
