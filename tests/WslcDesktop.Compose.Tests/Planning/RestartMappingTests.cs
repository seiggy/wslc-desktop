using WslcDesktop.Compose.Planning;

namespace WslcDesktop.Compose.Tests.Planning;

public class RestartMappingTests
{
    private static ComposePlan Plan(string restart) => ComposePlanner.Plan(
        $"services:\n  a:\n    image: alpine\n    restart: {restart}\n",
        new ComposeOptions { Directory = @"C:\work\demo", Env = new Dictionary<string, string>(), FileExists = _ => true });

    private static ComposeAction Run(ComposePlan plan) =>
        plan.Actions.Single(a => a.Kind == ActionKind.RunService);

    [Theory]
    [InlineData("always", "always")]
    [InlineData("unless-stopped", "unless-stopped")]
    [InlineData("on-failure", "on-failure")]
    [InlineData("on-failure:3", "on-failure:3")]
    public void Maps_restart_policies(string yaml, string expected)
    {
        var plan = Plan(yaml);
        Assert.Empty(plan.Warnings);
        Assert.Equal(expected, Run(plan).Restart);
    }

    [Theory]
    [InlineData("\"no\"")]
    [InlineData("false")]
    public void No_restart_means_no_policy(string yaml)
    {
        var plan = Plan(yaml);
        Assert.Empty(plan.Warnings);
        Assert.Null(Run(plan).Restart);
    }

    [Fact]
    public void Unknown_restart_value_warns()
    {
        var plan = Plan("sometimes");
        Assert.Null(Run(plan).Restart);
        Assert.Contains(plan.Warnings, w => w.Contains("restart"));
    }
}
