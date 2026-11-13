using WslcDesktop.Cli;
using WslcDesktop.Containers;
using WslcDesktop.Images;
using WslcDesktop.Localization;
using WslcDesktop.Resources;
using WslcDesktop.Sessions;

namespace WslcDesktop.Data;

/// <summary>
/// The app's background data layer. It keeps every visible session's containers, images, volumes, networks and
/// container inspections current, so pages read the latest values instantly and only listen for changes. Reads are
/// scheduled by <see cref="FeedScheduler"/>; <c>wslc events</c> streams, SDK callbacks and the app's own operations
/// mark exactly the affected data dirty, and polling is a safety net (or the fallback when a session has no events).
/// </summary>
public sealed class WslcStore : IDisposable
{
    private static readonly HashSet<string> ContainerActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "create", "start", "stop", "die", "kill", "destroy", "restart", "pause", "unpause", "rename", "update", "oom",
    };

    private static readonly HashSet<string> ResourceListActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "create", "destroy", "remove", "prune",
    };

    private readonly string _app;
    private readonly StoreSources _sources;
    private readonly TimeProvider _time;
    private readonly StorePolicies _p;
    private readonly IWslcProcess? _eventsProcess;
    private readonly Dictionary<string, SessionFeeds> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _resyncAfterList = new(StringComparer.OrdinalIgnoreCase);
    private readonly int[] _watchers = new int[Enum.GetValues<DataKind>().Length];
    private readonly Dictionary<string, Dictionary<string, int>> _charts = new(StringComparer.OrdinalIgnoreCase);
    private SessionEventStreams? _events;
    private volatile bool _nativeAppEvents;

    /// <param name="eventsProcess">Runs <c>wslc events</c>; null turns event streams off (tests, or polling only).</param>
    public WslcStore(string appSession, StoreSources sources, TimeProvider time, StorePolicies? policies = null, IWslcProcess? eventsProcess = null)
    {
        _app = appSession;
        _sources = sources;
        _time = time;
        _p = policies ?? new StorePolicies();
        _eventsProcess = eventsProcess;
        Scheduler = new FeedScheduler(time, _p.MaxConcurrentReads);

        SessionList = new Feed<IReadOnlyList<SessionRow>>(
            "sessions", ct => sources.ListSessions(ct), () => _p.Sessions, time, FeedEquality.Sequence, () => IsWatched(DataKind.Sessions));
        SessionList.Changed += () =>
        {
            SyncSessions();
            Raise(DataKind.Sessions);
        };
        SessionList.Loaded += ResyncSessionsThatLostEvents;

        Versions = new Feed<VersionInfo>(
            "versions", ct => sources.Versions(ct), () => _p.Versions, time, (a, b) => a == b, () => IsWatched(DataKind.Versions));
        Versions.Changed += () => Raise(DataKind.Versions);

        Scheduler.Add(SessionList);
        Scheduler.Add(Versions);
        Ensure(appSession);
    }

    public FeedScheduler Scheduler { get; }

    public Feed<IReadOnlyList<SessionRow>> SessionList { get; }

    public Feed<VersionInfo> Versions { get; }

    /// <summary>Raised (on any thread) when data of a kind changes in any session.</summary>
    public event Action<DataKind>? Changed;

    /// <summary>Raised (on any thread) after every fresh read of the app session's containers, changed or not.</summary>
    public event Action? AppContainersLoaded;

    public string AppSession => _app;

    /// <summary>Per-container CPU, memory and I/O history, appended to by every stats read.</summary>
    public TelemetryHistory Telemetry { get; } = new();

    /// <summary>The app session's containers; restart policies, the tray and stacks read these.</summary>
    public Feed<IReadOnlyList<ContainerRow>> AppContainers => Feeds(_app)!.Containers;

    /// <summary>True when the app owns its session and gets SDK callbacks, so it can poll less without an event stream.</summary>
    public bool NativeAppEvents
    {
        get => _nativeAppEvents;
        set
        {
            _nativeAppEvents = value;
            Scheduler.Wake();
        }
    }

    /// <summary>True while the window is hidden: only what the tray and restart policies need keeps refreshing.</summary>
    public bool Hidden
    {
        get => Scheduler.Hidden;
        set => Scheduler.Hidden = value;
    }

    public void Start()
    {
        Scheduler.Start();
        if (_eventsProcess is not null && _events is null)
        {
            _events = new SessionEventStreams(_eventsProcess, Apply, OnStreamState, _time);
            _events.Sync(Sessions);
        }
    }

    public void Stop()
    {
        Scheduler.Stop();
        _events?.Dispose();
        _events = null;
    }

    public void Dispose() => Stop();

    /// <summary>Visible sessions: the app's first, then the others by name. Without a session list, just the app's.</summary>
    public IReadOnlyList<string> Sessions =>
        (SessionList.Latest ?? [])
            .Select(s => s.Name)
            .Where(s => !IsApp(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .Prepend(_app)
            .ToList();

    public bool IsApp(string session) => string.Equals(session, _app, StringComparison.OrdinalIgnoreCase);

    public bool IsEventDriven(string session) => _events?.IsLive(session) == true;

    // Watching

    /// <summary>Uses the watched (faster) intervals for these kinds in every session until disposed.</summary>
    public IDisposable Watch(params DataKind[] kinds)
    {
        foreach (var kind in kinds)
        {
            Interlocked.Increment(ref _watchers[(int)kind]);
        }

        Scheduler.Wake();
        return new ReleaseHandle(() =>
        {
            foreach (var kind in kinds)
            {
                Interlocked.Decrement(ref _watchers[(int)kind]);
            }
        });
    }

    private bool IsWatched(DataKind kind) => Volatile.Read(ref _watchers[(int)kind]) > 0;

    /// <summary>
    /// Samples one container's stats at the chart rate until disposed. Unless the container list is showing too, only
    /// the charted containers are read.
    /// </summary>
    public IDisposable WatchStats(string session, string container)
    {
        lock (_charts)
        {
            if (!_charts.TryGetValue(session, out var names))
            {
                _charts[session] = names = new Dictionary<string, int>(StringComparer.Ordinal);
            }

            names[container] = names.GetValueOrDefault(container) + 1;
        }

        Scheduler.Wake();
        return new ReleaseHandle(() =>
        {
            lock (_charts)
            {
                if (_charts.TryGetValue(session, out var names) && names.TryGetValue(container, out var count))
                {
                    if (count <= 1)
                    {
                        names.Remove(container);
                    }
                    else
                    {
                        names[container] = count - 1;
                    }

                    if (names.Count == 0)
                    {
                        _charts.Remove(session);
                    }
                }
            }
        });
    }

    private HashSet<string> Charted(string session)
    {
        lock (_charts)
        {
            return _charts.TryGetValue(session, out var names) ? [.. names.Keys] : [];
        }
    }

    // Charts want a fast rate, the list a slower one, and nothing reads stats when neither shows them or when hidden.
    private FeedPolicy StatsPolicy(string session)
    {
        var interval = Charted(session).Count > 0 ? _p.StatsCharts : IsWatched(DataKind.Stats) ? _p.StatsList : FeedPolicy.Never;
        return new FeedPolicy(interval, interval, FeedPolicy.Never);
    }

    // Reading

    public SessionSnapshot<ContainerRow> Containers() => Merge(f => f.Containers);

    public ImageSnapshot Images()
    {
        var merged = Merge(f => f.Images);
        return new ImageSnapshot(
            merged.Items.Select(e => new SessionImage(e.Session, e.IsThisApp, e.Item)).ToList(),
            merged.Sessions,
            merged.Failures);
    }

    /// <summary>Volumes or networks, with "used by" taken from the session's container inspection.</summary>
    public SessionSnapshot<ResourceRow> Resources(ResourceKind kind) =>
        Merge(
            f => kind == ResourceKind.Volume ? f.Volumes : f.Networks,
            (f, row) => f.Inspect.Latest is { } inspection ? row with { UsedBy = inspection.Usage.For(kind, row.Name) } : row);

    public IReadOnlyList<ContainerRow>? ContainersOf(string session) => Feeds(session)?.Containers.Latest;

    public ContainerRow? FindContainer(string session, string name) =>
        ContainersOf(session)?.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal));

    public IReadOnlyList<ImageSummary>? ImagesOf(string session) => Feeds(session)?.Images.Latest;

    public IReadOnlyList<string>? NetworkNamesOf(string session) => Feeds(session)?.Networks.Latest?.Select(r => r.Name).ToList();

    /// <summary>Unredacted inspect JSON for a container, or null if it hasn't been inspected yet.</summary>
    public string? InspectJson(string session, string container) =>
        Feeds(session)?.Inspect.Latest?.Json.GetValueOrDefault(container);

    public bool HasLoaded(DataKind kind, string session) => FeedOf(kind, session)?.HasValue == true;

    public string? LastError(DataKind kind, string session) => FeedOf(kind, session)?.LastError;

    // Freshness

    /// <summary>Asks for a fresh read of one kind of data in one session; the current value stays until it arrives.</summary>
    public void Invalidate(DataKind kind, string session)
    {
        switch (kind)
        {
            case DataKind.Sessions:
                SessionList.MarkDirty();
                break;
            case DataKind.Versions:
                Versions.MarkDirty();
                break;
            default:
                FeedOf(kind, session)?.MarkDirty();
                break;
        }
    }

    /// <summary>Reads now (for a refresh button, or after an action that should show its result straight away).</summary>
    public async Task RefreshAsync(DataKind kind, string? session = null, CancellationToken ct = default)
    {
        switch (kind)
        {
            case DataKind.Sessions:
                await SessionList.RefreshAsync(ct).ConfigureAwait(false);
                return;
            case DataKind.Versions:
                await Versions.RefreshAsync(ct).ConfigureAwait(false);
                return;
        }

        var sessions = session is null ? Sessions : [session];
        await Task.WhenAll(sessions.Select(async s =>
        {
            if (Feeds(s) is not { } f)
            {
                return;
            }

            if (kind == DataKind.Inspect)
            {
                // The inspection covers the containers listed, so a new container needs the list first.
                await f.Containers.RefreshAsync(ct).ConfigureAwait(false);
            }

            await FeedOf(f, kind).RefreshAsync(ct).ConfigureAwait(false);
        })).ConfigureAwait(false);
    }

    // Events

    /// <summary>Marks what one event changed. Public so the mapping can be tested without a stream.</summary>
    public void Apply(string session, WslcEvent e)
    {
        if (Feeds(session) is not { } f)
        {
            return;
        }

        switch (e.Type.ToLowerInvariant())
        {
            case "container" when ContainerActions.Contains(e.Action):
                f.Containers.MarkDirty();
                f.Inspect.MarkDirty();
                if (e.Action.Equals("create", StringComparison.OrdinalIgnoreCase) || e.Action.Equals("destroy", StringComparison.OrdinalIgnoreCase))
                {
                    // Image rows count the containers using them.
                    f.Images.MarkDirty();
                }

                break;
            case "network" when ResourceListActions.Contains(e.Action):
                f.Networks.MarkDirty();
                break;
            case "network" when e.Action is "connect" or "disconnect":
                f.Inspect.MarkDirty();
                break;
            case "volume" when ResourceListActions.Contains(e.Action):
                f.Volumes.MarkDirty();
                break;
            case "image":
                f.Images.MarkDirty();
                break;
        }
    }

    private void OnStreamState(string session, StreamState state)
    {
        // Intervals depend on whether the session's stream is live.
        Scheduler.Wake();
        switch (state)
        {
            case StreamState.Reconnected:
                Feeds(session)?.MarkAllDirty();
                break;
            case StreamState.Lost:
                // Usually the session was terminated: confirm with the session list before re-reading everything.
                lock (_resyncAfterList)
                {
                    _resyncAfterList.Add(session);
                }

                SessionList.MarkDirty();
                break;
            case StreamState.Failed:
                SessionList.MarkDirty();
                break;
        }
    }

    private void ResyncSessionsThatLostEvents()
    {
        List<string> pending;
        lock (_resyncAfterList)
        {
            pending = [.. _resyncAfterList];
            _resyncAfterList.Clear();
        }

        foreach (var session in pending)
        {
            Feeds(session)?.MarkAllDirty();
        }
    }

    // Sessions

    private void SyncSessions()
    {
        var names = Sessions;
        foreach (var name in names)
        {
            Ensure(name);
        }

        List<SessionFeeds> gone;
        lock (_sessions)
        {
            gone = _sessions.Where(kv => !names.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)).Select(kv => kv.Value).ToList();
            foreach (var feeds in gone)
            {
                _sessions.Remove(feeds.Session);
            }
        }

        foreach (var feed in gone.SelectMany(f => f.All))
        {
            Scheduler.Remove(feed);
        }

        foreach (var feeds in gone)
        {
            Telemetry.Forget(feeds.Session);
        }

        _events?.Sync(names);
        foreach (var kind in new[] { DataKind.Containers, DataKind.Inspect, DataKind.Images, DataKind.Volumes, DataKind.Networks })
        {
            Raise(kind);
        }
    }

    private void Ensure(string session)
    {
        SessionFeeds f;
        lock (_sessions)
        {
            if (_sessions.ContainsKey(session))
            {
                return;
            }

            f = Create(session);
            _sessions[session] = f;
        }

        foreach (var feed in f.All)
        {
            Scheduler.Add(feed);
        }
    }

    private SessionFeeds Create(string s)
    {
        Feed<IReadOnlyList<TItem>> List<TItem>(DataKind kind, Func<CancellationToken, Task<IReadOnlyList<TItem>>> load, Func<FeedPolicy> policy, Func<IReadOnlyList<TItem>, IReadOnlyList<TItem>, bool>? same = null) =>
            new($"{kind}:{s}", load, policy, _time, same ?? FeedEquality.Sequence, () => IsWatched(kind));

        var isApp = IsApp(s);
        var containers = List(DataKind.Containers, ct => _sources.ListContainers(s, ct), () => ContainersPolicy(s));
        var images = List(DataKind.Images, ct => _sources.ListImages(s, ct), () => _p.Images);
        var volumes = List(DataKind.Volumes, ct => _sources.ListResources(s, ResourceKind.Volume, ct), () => ResourcesPolicy(s), FeedEquality.Resources);
        var networks = List(DataKind.Networks, ct => _sources.ListResources(s, ResourceKind.Network, ct), () => ResourcesPolicy(s), FeedEquality.Resources);
        var inspect = new Feed<ContainerInspection>(
            $"{DataKind.Inspect}:{s}",
            async ct =>
            {
                // Waits for the container list rather than reading it twice at startup; its first change marks this dirty.
                var rows = containers.Latest;
                if (rows is null || rows.Count == 0)
                {
                    return ContainerInspection.Empty;
                }

                return ContainerInspection.From(await _sources.InspectContainers(s, rows.Select(r => r.Name).ToList(), ct).ConfigureAwait(false));
            },
            () => IsEventDriven(s) ? _p.InspectWithEvents : _p.InspectPolling,
            _time,
            ContainerInspection.Same,
            () => IsWatched(DataKind.Inspect));

        containers.Changed += () =>
        {
            inspect.MarkDirty();
            Raise(DataKind.Containers);
        };
        if (isApp)
        {
            containers.Loaded += () => AppContainersLoaded?.Invoke();
        }

        inspect.Changed += () =>
        {
            Raise(DataKind.Inspect);
            Raise(DataKind.Volumes);
            Raise(DataKind.Networks);
        };
        images.Changed += () => Raise(DataKind.Images);
        volumes.Changed += () => Raise(DataKind.Volumes);
        networks.Changed += () => Raise(DataKind.Networks);

        var stats = new Feed<IReadOnlyDictionary<string, StatsSample>>(
            $"{DataKind.Stats}:{s}",
            async ct =>
            {
                var running = (containers.Latest ?? [])
                    .Where(r => r.State.Equals("running", StringComparison.OrdinalIgnoreCase))
                    .Select(r => r.Name)
                    .ToHashSet(StringComparer.Ordinal);

                // The list shows every running container; charts alone need only theirs.
                IReadOnlyList<string>? targets = null;
                if (!IsWatched(DataKind.Stats))
                {
                    targets = Charted(s).Where(running.Contains).ToList();
                }

                IReadOnlyDictionary<string, StatsSample> samples = running.Count == 0 || targets is { Count: 0 }
                    ? new Dictionary<string, StatsSample>()
                    : await _sources.Stats(s, targets, ct).ConfigureAwait(false);
                Telemetry.Record(s, running, samples);
                return samples;
            },
            () => StatsPolicy(s),
            _time);
        stats.Loaded += () => Raise(DataKind.Stats);
        stats.Changed += () => Raise(DataKind.Stats);
        return new SessionFeeds(s, containers, inspect, images, volumes, networks, stats);
    }

    private FeedPolicy ContainersPolicy(string session)
    {
        var events = IsEventDriven(session);
        if (IsApp(session))
        {
            return events ? _p.AppContainersWithEvents : _nativeAppEvents ? _p.AppContainersWithSdk : _p.AppContainersPolling;
        }

        return events ? _p.ContainersWithEvents : _p.ContainersPolling;
    }

    private FeedPolicy ResourcesPolicy(string session) => IsEventDriven(session) ? _p.ResourcesWithEvents : _p.ResourcesPolling;

    private SessionFeeds? Feeds(string session)
    {
        lock (_sessions)
        {
            return _sessions.GetValueOrDefault(session);
        }
    }

    private IScheduledFeed? FeedOf(DataKind kind, string session) => kind switch
    {
        DataKind.Sessions => SessionList,
        DataKind.Versions => Versions,
        _ => Feeds(session) is { } f ? FeedOf(f, kind) : null,
    };

    private static IScheduledFeed FeedOf(SessionFeeds f, DataKind kind) => kind switch
    {
        DataKind.Containers => f.Containers,
        DataKind.Inspect => f.Inspect,
        DataKind.Images => f.Images,
        DataKind.Volumes => f.Volumes,
        DataKind.Networks => f.Networks,
        DataKind.Stats => f.Stats,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private SessionSnapshot<T> Merge<T>(
        Func<SessionFeeds, Feed<IReadOnlyList<T>>> pick,
        Func<SessionFeeds, T, T>? map = null)
    {
        var sessions = Sessions;
        var items = new List<SessionEntry<T>>();
        var failures = new List<SessionFailure>();
        if (SessionList.LastError is { } listError)
        {
            failures.Add(new SessionFailure(UiStrings.Get("SessionListSource"), listError));
        }

        foreach (var session in sessions)
        {
            if (Feeds(session) is not { } f)
            {
                continue;
            }

            var feed = pick(f);
            if (feed.LastError is { } error)
            {
                failures.Add(new SessionFailure(session, error));
            }

            if (feed.Latest is not { } latest)
            {
                continue;
            }

            var isApp = IsApp(session);
            items.AddRange(latest.Select(item => new SessionEntry<T>(session, isApp, map is null ? item : map(f, item))));
        }

        return new SessionSnapshot<T>(items, sessions, failures);
    }

    private void Raise(DataKind kind) => Changed?.Invoke(kind);

    private sealed record SessionFeeds(
        string Session,
        Feed<IReadOnlyList<ContainerRow>> Containers,
        Feed<ContainerInspection> Inspect,
        Feed<IReadOnlyList<ImageSummary>> Images,
        Feed<IReadOnlyList<ResourceRow>> Volumes,
        Feed<IReadOnlyList<ResourceRow>> Networks,
        Feed<IReadOnlyDictionary<string, StatsSample>> Stats)
    {
        public IEnumerable<IScheduledFeed> All => [Containers, Inspect, Images, Volumes, Networks, Stats];

        public void MarkAllDirty()
        {
            Containers.MarkDirty();
            Inspect.MarkDirty();
            Images.MarkDirty();
            Volumes.MarkDirty();
            Networks.MarkDirty();
        }
    }
}
