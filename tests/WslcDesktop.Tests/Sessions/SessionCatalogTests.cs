
using WslcDesktop.Sessions;

namespace WslcDesktop.Tests.Sessions;

public class SessionCatalogTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Rig
    {
        public Clock Clock { get; } = new();
        public Dictionary<string, int> Calls { get; } = [];
        public Dictionary<string, Func<IReadOnlyList<string>>> Sources { get; } = [];
        public bool SessionListFails { get; set; }
        public SessionCatalog<string> Catalog { get; }

        public Rig(params string[] sessions)
        {
            Catalog = new SessionCatalog<string>(
                "WslcDesktop",
                () => SessionListFails ? throw new InvalidOperationException("no list") : Task.FromResult<IReadOnlyList<string>>(sessions),
                s =>
                {
                    Calls[s] = Calls.GetValueOrDefault(s) + 1;
                    return Task.FromResult(Sources[s]());
                },
                Clock,
                TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task App_session_is_first_and_flagged()
    {
        var rig = new Rig("zeta", "WslcDesktop", "alpha");
        rig.Sources["WslcDesktop"] = () => ["app"];
        rig.Sources["zeta"] = () => ["z"];
        rig.Sources["alpha"] = () => ["a"];

        var snap = await rig.Catalog.RefreshAsync();

        Assert.Equal(["WslcDesktop", "alpha", "zeta"], snap.Sessions);
        Assert.Equal(["app", "a", "z"], snap.Items.Select(i => i.Item));
        Assert.True(snap.Items[0].IsThisApp);
    }

    [Fact]
    public async Task Other_sessions_are_throttled_but_app_session_is_always_read()
    {
        var rig = new Rig("WslcDesktop", "cli");
        rig.Sources["WslcDesktop"] = () => ["app"];
        rig.Sources["cli"] = () => ["c"];

        await rig.Catalog.RefreshAsync();
        rig.Clock.Now += TimeSpan.FromSeconds(2);
        await rig.Catalog.RefreshAsync();

        Assert.Equal(2, rig.Calls["WslcDesktop"]);
        Assert.Equal(1, rig.Calls["cli"]);

        rig.Clock.Now += TimeSpan.FromSeconds(20);
        await rig.Catalog.RefreshAsync();
        Assert.Equal(2, rig.Calls["cli"]);
    }

    [Fact]
    public async Task Force_and_invalidate_bypass_the_throttle()
    {
        var rig = new Rig("WslcDesktop", "cli");
        rig.Sources["WslcDesktop"] = () => ["app"];
        rig.Sources["cli"] = () => ["c"];

        await rig.Catalog.RefreshAsync();
        await rig.Catalog.RefreshAsync(force: true);
        Assert.Equal(2, rig.Calls["cli"]);

        rig.Catalog.Invalidate("cli");
        await rig.Catalog.RefreshAsync();
        Assert.Equal(3, rig.Calls["cli"]);
    }

    [Fact]
    public async Task Failing_session_keeps_stale_items_and_reports_failure()
    {
        var rig = new Rig("WslcDesktop", "cli");
        rig.Sources["WslcDesktop"] = () => ["app"];
        rig.Sources["cli"] = () => ["c"];
        await rig.Catalog.RefreshAsync();

        rig.Sources["cli"] = () => throw new InvalidOperationException("denied");
        var snap = await rig.Catalog.RefreshAsync(force: true);

        Assert.Equal(["app", "c"], snap.Items.Select(i => i.Item));
        var failure = Assert.Single(snap.Failures);
        Assert.Equal("cli", failure.Session);
        Assert.Contains("denied", failure.Message);
    }

    [Fact]
    public async Task Session_list_failure_falls_back_to_app_session()
    {
        var rig = new Rig("WslcDesktop", "cli") { SessionListFails = true };
        rig.Sources["WslcDesktop"] = () => ["app"];

        var snap = await rig.Catalog.RefreshAsync();

        Assert.Equal(["WslcDesktop"], snap.Sessions);
        Assert.Equal(["app"], snap.Items.Select(i => i.Item));
    }
}
