
using WslcDesktop.Compose.Execution;
using WslcDesktop.Compose.Planning;

namespace WslcDesktop.Compose.Tests.Execution;

public class RunnerTests
{
    private sealed class FakeWslc
    {
        public List<string> Calls { get; } = [];

        public int HealthPolls { get; private set; }

        public Task<string> Exec(IReadOnlyList<string> args)
        {
            Calls.Add(string.Join(' ', args));
            switch (args[0])
            {
                case "list" or "network" or "volume" when args.Contains("--format"):
                    return Task.FromResult(string.Empty);
                case "inspect":
                    HealthPolls++;
                    var status = HealthPolls < 3 ? "starting" : "healthy";
                    return Task.FromResult($"[{{\"State\":{{\"Status\":\"running\",\"ExitCode\":0,\"Health\":{{\"Status\":\"{status}\"}}}}}}]");
                default:
                    return Task.FromResult(string.Empty);
            }
        }
    }

    [Fact]
    public async Task Up_creates_resources_then_waits_for_health_before_starting_dependents()
    {
        const string yaml = """
            services:
              web: { image: nginx, depends_on: { db: { condition: service_healthy } } }
              db: { image: postgres, healthcheck: { test: ["CMD", "true"] } }
            """;
        var plan = ComposePlanner.Plan(yaml, new ComposeOptions { ProjectName = "p", Directory = "C:\\x", FileExists = _ => true });
        var fake = new FakeWslc();
        var runner = new ComposeRunner(fake.Exec, _ => { }) { PollInterval = TimeSpan.FromMilliseconds(1) };

        await runner.UpAsync(plan);

        var run = fake.Calls.Where(c => c.StartsWith("run ", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, run.Count);
        Assert.Contains("--name p-db", run[0]);
        Assert.Contains("--name p-web", run[1]);
        Assert.True(fake.HealthPolls >= 3);
        Assert.True(fake.Calls.FindIndex(c => c.StartsWith("network create", StringComparison.Ordinal))
            < fake.Calls.FindIndex(c => c.StartsWith("run ", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Up_refuses_a_plan_with_errors()
    {
        var plan = ComposePlanner.Plan("services:\n  a: {}\n", new ComposeOptions { ProjectName = "p", Directory = "C:\\x" });
        var runner = new ComposeRunner(new FakeWslc().Exec, _ => { });

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.UpAsync(plan));
    }
}
