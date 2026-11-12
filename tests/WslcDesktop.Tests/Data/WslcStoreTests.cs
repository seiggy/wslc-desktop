using WslcDesktop.Containers;
using WslcDesktop.Data;
using WslcDesktop.Images;
using WslcDesktop.Resources;
using WslcDesktop.Sessions;

namespace WslcDesktop.Tests.Data;

public class WslcStoreTests
{
    private const string App = "WslcDesktop";

    /// <summary>A store over in-memory sources; each kind is a function of the session so tests can change answers.</summary>
    private sealed class Rig
    {
        public ManualClock Clock { get; } = new();
        public List<string> Listed { get; set; } = [App];
        public Exception? SessionListError { get; set; }
        public Dictionary<string, Func<IReadOnlyList<ContainerRow>>> Containers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Func<IReadOnlyList<ImageSummary>>> Images { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Func<IReadOnlyList<ResourceRow>>> Volumes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Func<IReadOnlyDictionary<string, string>>> Inspect { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Reads { get; } = [];
        public WslcStore Store { get; }

        public Rig()
        {
            Store = new WslcStore(
                App,
                new StoreSources(
                    _ => SessionListError is { } e
                        ? Task.FromException<IReadOnlyList<SessionRow>>(e)
                        : Task.FromResult<IReadOnlyList<SessionRow>>(Listed.Select((n, i) => new SessionRow($"{i}", "1", n)).ToList()),
                    (s, _) => Read("containers", s, Containers, []),
                    (s, _) => Read("images", s, Images, []),
                    (s, kind, _) => kind == ResourceKind.Volume ? Read("volumes", s, Volumes, []) : Read<ResourceRow>("networks", s, [], []),
                    (s, names, _) => Read("inspect", s, Inspect, (IReadOnlyDictionary<string, string>)new Dictionary<string, string>()),
                    _ => Task.FromResult(new VersionInfo("1", "2", "3")),
                    (s, targets, _) =>
                    {
                        lock (Reads)
                        {
                            Reads.Add($"stats:{s}");
                            StatsTargets.Add(targets);
                        }

                        var names = targets ?? (Containers.TryGetValue(s, out var f) ? f().Where(c => c.State == "running").Select(c => c.Name).ToList() : []);
                        return Task.FromResult<IReadOnlyDictionary<string, StatsSample>>(names.ToDictionary(n => n, n => Sample(Clock.Now)));
                    }),
                Clock);
        }

        public List<IReadOnlyList<string>?> StatsTargets { get; } = [];

        private Task<IReadOnlyList<T>> Read<T>(string kind, string s, Dictionary<string, Func<IReadOnlyList<T>>> map, IReadOnlyList<T> fallback)
        {
            lock (Reads)
            {
                Reads.Add($"{kind}:{s}");
            }

            try
            {
                return Task.FromResult(map.TryGetValue(s, out var f) ? f() : fallback);
            }
            catch (Exception e)
            {
                return Task.FromException<IReadOnlyList<T>>(e);
            }
        }

        private Task<T> Read<T>(string kind, string s, Dictionary<string, Func<T>> map, T fallback)
        {
            lock (Reads)
            {
                Reads.Add($"{kind}:{s}");
            }

            return Task.FromResult(map.TryGetValue(s, out var f) ? f() : fallback);
        }

        public int ReadCount(string read)
        {
            lock (Reads)
            {
                return Reads.Count(r => r == read);
            }
        }

        public async Task LoadAllAsync()
        {
            await Store.RefreshAsync(DataKind.Sessions);
            foreach (var kind in new[] { DataKind.Containers, DataKind.Inspect, DataKind.Images, DataKind.Volumes, DataKind.Networks })
            {
                await Store.RefreshAsync(kind);
            }
        }
    }

    private static ContainerRow Row(string name, string state = "running") => new(name, "img", name, state, "", "");

    private static StatsSample Sample(DateTimeOffset at) => new(at, 1, 2, 3, 4, 5, 6, 7, 8, 9);

    private static async Task TickAsync(Rig rig, TimeSpan by)
    {
        rig.Clock.Advance(by);
        await rig.Store.Scheduler.RunDueAsync();
    }

    [Fact]
    public async Task Stats_are_not_read_while_nothing_shows_them()
    {
        var rig = new Rig();
        rig.Containers[App] = () => [Row("web")];
        await rig.LoadAllAsync();

        await TickAsync(rig, TimeSpan.FromMinutes(5));

        Assert.Equal(0, rig.ReadCount($"stats:{App}"));
    }

    [Fact]
    public async Task The_list_reads_every_running_container_at_the_list_rate()
    {
        var rig = new Rig();
        rig.Containers[App] = () => [Row("web"), Row("db", "exited")];
        await rig.LoadAllAsync();

        using (rig.Store.Watch(DataKind.Stats))
        {
            await TickAsync(rig, TimeSpan.Zero);
            await TickAsync(rig, TimeSpan.FromSeconds(3));
            await TickAsync(rig, TimeSpan.FromSeconds(2));
        }

        Assert.Equal(2, rig.ReadCount($"stats:{App}"));
        Assert.All(rig.StatsTargets, t => Assert.Null(t));
        Assert.Equal(2, rig.Store.Telemetry.GetHistory(App, "web").Count);
        Assert.Empty(rig.Store.Telemetry.GetHistory(App, "db"));
    }

    [Fact]
    public async Task Charts_alone_read_only_their_container_at_the_chart_rate()
    {
        var rig = new Rig { Listed = [App, "lab"] };
        rig.Containers[App] = () => [Row("web"), Row("api")];
        rig.Containers["lab"] = () => [Row("other")];
        await rig.LoadAllAsync();

        using (rig.Store.WatchStats(App, "web"))
        {
            await TickAsync(rig, TimeSpan.Zero);
            await TickAsync(rig, TimeSpan.FromSeconds(2));
        }

        Assert.Equal(2, rig.ReadCount($"stats:{App}"));
        Assert.Equal(0, rig.ReadCount("stats:lab"));
        Assert.All(rig.StatsTargets, t => Assert.Equal(["web"], t!));
    }

    [Fact]
    public async Task Stats_stop_while_hidden_and_for_stopped_containers()
    {
        var rig = new Rig();
        var state = "running";
        rig.Containers[App] = () => [Row("web", state)];
        await rig.LoadAllAsync();
        using var watch = rig.Store.Watch(DataKind.Stats);
        await TickAsync(rig, TimeSpan.Zero);
        Assert.Single(rig.Store.Telemetry.GetHistory(App, "web"));

        rig.Store.Hidden = true;
        await TickAsync(rig, TimeSpan.FromMinutes(1));
        Assert.Equal(1, rig.ReadCount($"stats:{App}"));

        rig.Store.Hidden = false;
        state = "exited";
        await rig.Store.RefreshAsync(DataKind.Containers, App);
        await TickAsync(rig, TimeSpan.FromSeconds(10));

        // Nothing running: no wslc call, and the stopped container's old samples are gone.
        Assert.Equal(1, rig.ReadCount($"stats:{App}"));
        Assert.Empty(rig.Store.Telemetry.GetHistory(App, "web"));
    }

    private static ImageSummary Img(string repo) => new(repo, "latest", "sha256:aaaaaaaaaaaaaaaa", string.Empty, "1MB", "1 day ago", 0);

    [Fact]
    public async Task Snapshots_put_the_app_session_first_then_the_others_by_name()
    {
        var rig = new Rig { Listed = ["zeta", App, "alpha"] };
        rig.Containers[App] = () => [Row("app")];
        rig.Containers["zeta"] = () => [Row("z")];
        rig.Containers["alpha"] = () => [Row("a")];

        await rig.LoadAllAsync();
        var snap = rig.Store.Containers();

        Assert.Equal([App, "alpha", "zeta"], snap.Sessions);
        Assert.Equal(["app", "a", "z"], snap.Items.Select(i => i.Item.Name));
        Assert.True(snap.Items[0].IsThisApp);
        Assert.All(snap.Items.Skip(1), i => Assert.False(i.IsThisApp));
    }

    [Fact]
    public async Task A_failing_session_keeps_its_last_rows_and_reports_the_failure()
    {
        var rig = new Rig { Listed = [App, "other"] };
        rig.Images["other"] = () => [Img("cli")];
        await rig.LoadAllAsync();

        rig.Images["other"] = () => throw new InvalidOperationException("denied");
        await rig.Store.RefreshAsync(DataKind.Images, "other");
        var snap = rig.Store.Images();

        Assert.Contains(snap.Images, i => i.Session == "other");
        var failure = Assert.Single(snap.Failures);
        Assert.Equal(("other", "denied"), (failure.Session, failure.Message));
    }

    [Fact]
    public async Task Without_a_session_list_only_the_app_session_is_shown()
    {
        var rig = new Rig { SessionListError = new InvalidOperationException("no wslc") };
        rig.Images[App] = () => [Img("x")];
        await rig.LoadAllAsync();

        var snap = rig.Store.Images();

        Assert.Equal([App], snap.Sessions);
        Assert.True(Assert.Single(snap.Images).IsThisApp);
        Assert.Single(snap.Failures);
    }

    [Fact]
    public async Task A_vanished_session_drops_its_rows()
    {
        var rig = new Rig { Listed = [App, "other"] };
        rig.Images["other"] = () => [Img("x")];
        await rig.LoadAllAsync();
        Assert.Single(rig.Store.Images().Images);

        rig.Listed = [App];
        await rig.Store.RefreshAsync(DataKind.Sessions);

        Assert.Empty(rig.Store.Images().Images);
        Assert.Null(rig.Store.ImagesOf("other"));
    }

    [Fact]
    public async Task Rows_read_before_any_change_are_shown_without_waiting_and_not_cleared_by_invalidation()
    {
        var rig = new Rig();
        rig.Images[App] = () => [Img("x")];
        await rig.LoadAllAsync();

        rig.Store.Invalidate(DataKind.Images, App);

        Assert.Single(rig.Store.Images().Images);
    }

    [Fact]
    public async Task Volume_and_network_usage_comes_from_the_container_inspection()
    {
        var rig = new Rig();
        rig.Containers[App] = () => [Row("web"), Row("db")];
        rig.Volumes[App] = () => [new ResourceRow("data", "local", "/v", "local", true, [])];
        rig.Inspect[App] = () => new Dictionary<string, string>
        {
            ["web"] = """{"Mounts":[{"Type":"volume","Name":"data"}]}""",
            ["db"] = """[{"Mounts":[{"Type":"volume","Name":"data"},{"Type":"bind","Name":""}]}]""",
        };
        await rig.LoadAllAsync();

        var row = Assert.Single(rig.Store.Resources(ResourceKind.Volume).Items);

        Assert.Equal(["web", "db"], row.Item.UsedBy);
        Assert.Contains("\"data\"", rig.Store.InspectJson(App, "web"));
    }

    [Fact]
    public async Task The_inspection_waits_for_the_container_list_instead_of_reading_it_again()
    {
        var rig = new Rig();
        rig.Containers[App] = () => [Row("web")];

        await rig.Store.RefreshAsync(DataKind.Sessions);
        await rig.Store.Scheduler.RunDueAsync();
        rig.Clock.Advance(Feed<string>.Debounce);
        await rig.Store.Scheduler.RunDueAsync();

        // Whichever finishes first, the list is read once and the inspection covers it once.
        Assert.Equal(1, rig.ReadCount($"containers:{App}"));
        Assert.Equal(1, rig.ReadCount($"inspect:{App}"));
        Assert.True(rig.Store.HasLoaded(DataKind.Inspect, App));
    }

    [Theory]
    [InlineData("container", "start", new[] { DataKind.Containers, DataKind.Inspect })]
    [InlineData("container", "die", new[] { DataKind.Containers, DataKind.Inspect })]
    [InlineData("container", "create", new[] { DataKind.Containers, DataKind.Inspect, DataKind.Images })]
    [InlineData("container", "destroy", new[] { DataKind.Containers, DataKind.Inspect, DataKind.Images })]
    [InlineData("container", "exec_start", new DataKind[0])]
    [InlineData("network", "create", new[] { DataKind.Networks })]
    [InlineData("network", "connect", new[] { DataKind.Inspect })]
    [InlineData("volume", "destroy", new[] { DataKind.Volumes })]
    [InlineData("volume", "mount", new DataKind[0])]
    [InlineData("image", "pull", new[] { DataKind.Images })]
    public async Task Events_mark_exactly_the_affected_data_dirty(string type, string action, DataKind[] expected)
    {
        var rig = new Rig { Listed = [App, "lab"] };
        rig.Containers["lab"] = () => [Row("c")];
        await rig.LoadAllAsync();
        var kinds = new[] { DataKind.Containers, DataKind.Inspect, DataKind.Images, DataKind.Volumes, DataKind.Networks };
        var before = kinds.ToDictionary(k => k, k => rig.ReadCount($"{Read(k)}:lab"));
        var appBefore = rig.Reads.Count(r => r.EndsWith($":{App}", StringComparison.Ordinal));

        rig.Store.Apply("lab", new WslcEvent(type, action, "id", "name"));
        rig.Clock.Advance(Feed<string>.Debounce);
        await rig.Store.Scheduler.RunDueAsync();

        var reread = kinds.Where(k => rig.ReadCount($"{Read(k)}:lab") > before[k]).ToArray();
        Assert.Equal(expected.Order(), reread.Order());
        Assert.Equal(appBefore, rig.Reads.Count(r => r.EndsWith($":{App}", StringComparison.Ordinal)));
    }

    private static string Read(DataKind kind) => kind switch
    {
        DataKind.Containers => "containers",
        DataKind.Inspect => "inspect",
        DataKind.Images => "images",
        DataKind.Volumes => "volumes",
        _ => "networks",
    };

    [Fact]
    public async Task Changes_are_announced_once_per_real_change()
    {
        var rig = new Rig();
        rig.Images[App] = () => [Img("x")];
        await rig.LoadAllAsync();
        var announced = new List<DataKind>();
        rig.Store.Changed += announced.Add;

        await rig.Store.RefreshAsync(DataKind.Images, App);
        Assert.DoesNotContain(DataKind.Images, announced);

        rig.Images[App] = () => [Img("x"), Img("y")];
        await rig.Store.RefreshAsync(DataKind.Images, App);
        Assert.Single(announced, k => k == DataKind.Images);
    }

    [Fact]
    public async Task Watching_a_kind_uses_its_faster_interval_in_every_session()
    {
        var rig = new Rig { Listed = [App, "lab"] };
        await rig.LoadAllAsync();
        var before = rig.ReadCount("images:lab");

        rig.Clock.Advance(TimeSpan.FromSeconds(20));
        await rig.Store.Scheduler.RunDueAsync();
        Assert.Equal(before, rig.ReadCount("images:lab"));

        using (rig.Store.Watch(DataKind.Images))
        {
            await rig.Store.Scheduler.RunDueAsync();
        }

        Assert.Equal(before + 1, rig.ReadCount("images:lab"));
    }

    [Fact]
    public async Task Every_fresh_read_of_the_app_containers_is_reported_even_without_a_change()
    {
        var rig = new Rig();
        rig.Containers[App] = () => [Row("web")];
        var loads = 0;
        rig.Store.AppContainersLoaded += () => loads++;

        await rig.Store.RefreshAsync(DataKind.Containers, App);
        await rig.Store.RefreshAsync(DataKind.Containers, App);

        Assert.Equal(2, loads);
    }
}
