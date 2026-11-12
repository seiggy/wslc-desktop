using Microsoft.WSL.Containers;
using WslcDesktop.Backends;
using WslcDesktop.Cli;
using WslcDesktop.Sdk;
using WslcDesktop.Common;
using WslcDesktop.Compose.Execution;
using WslcDesktop.Compose.Secrets.Injection;
using WslcDesktop.Containers;
using WslcDesktop.Containers.Creation;
using WslcDesktop.Data;
using WslcDesktop.Images;
using WslcDesktop.Lifecycle;
using WslcDesktop.Resources;
using WslcDesktop.Restart;
using WslcDesktop.Restart.Monitoring;
using WslcDesktop.Restart.Persistence;
using WslcDesktop.Secrets;
using WslcDesktop.Sessions;
using WslcDesktop.Settings;

namespace WslcDesktop.Services;

/// <summary>
/// Owns the single WSLc session and wraps the synchronous Microsoft.WSL.Containers API
/// so every call runs off the UI thread.
/// </summary>
public sealed class WslcHost
{
    private static readonly string SessionName = DefaultSession.Name();

    private readonly string _root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WslcDesktop");

    private Session? _session;

    private bool _attached;
    private readonly CancellationTokenSource _lifetime = new();

    /// <summary>True when an earlier app run left the session alive and this run drives it through wslc.exe.</summary>
    public bool IsAttached => _attached;

    public bool IsReady => _session is not null || _attached;

    public string SessionDisplayName => SessionName;

    public Task<IReadOnlyList<Component>> GetMissingComponentsAsync() =>
        Task.Run(() => WslcService.GetMissingComponents());

    public Task<string> GetVersionAsync() => Task.Run(() =>
    {
        var v = WslcService.GetVersion();
        return $"{v.Major}.{v.Minor}.{v.Revision}";
    });

    public async Task InstallMissingAsync(IReadOnlyList<Component> missing, IProgress<InstallProgress> progress)
    {
        var op = WslcService.InstallWithDependenciesAsync(new InstallOptions { Components = missing });
        op.Progress = (_, p) => progress.Report(p);
        await op;
    }

    public AppSettings Settings { get; } = AppSettings.Load();

    public WslcHost()
    {
        _router = new BackendRouter(owned: null, CliBackend);
        Operations = new WslcOperations(_router, new HostEffects(this));
        Restart = new RestartEngine(new RestartPolicyStore(), new HostRestartActions(this), TimeProvider.System);
        Store = new WslcStore(SessionName, CreateSources(), TimeProvider.System, eventsProcess: WslcProcess.Default);
        Store.AppContainersLoaded += OnAppContainersLoaded;
    }

    /// <summary>App-level restart policies. They only run while this app is running.</summary>
    public RestartEngine Restart { get; }

    /// <summary>Why the WSLc session ended, when this app owns it and the API reported it (attached mode can't see this).</summary>
    public string? SessionEndedReason { get; private set; }

    // Restart policies run on every fresh read of the app's containers, changed or not.
    private void OnAppContainersLoaded()
    {
        if (Store.AppContainers.Latest is not { } containers)
        {
            return;
        }

        var observations = containers
            .Select(c => new ContainerObservation(c.Name, c.State, ContainerStatusText.ExitCode(c.Status)))
            .ToList();
        _ = ProcessRestartsAsync(observations);
    }

    private async Task ProcessRestartsAsync(IReadOnlyList<ContainerObservation> observations)
    {
        try
        {
            await Restart.ProcessAsync(observations, _lifetime.Token);
            ScheduleRestartCheck();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // A failed restart pass is retried on the next snapshot.
        }
    }

    private CancellationTokenSource? _restartWake;

    private static readonly TimeSpan MinRestartCheckDelay = TimeSpan.FromMilliseconds(250);

    // Snapshots mostly arrive on changes, so when the engine needs one (a backed-off restart coming due, a start grace
    // ending, a retry after a failed start) it is taken right then.
    private void ScheduleRestartCheck()
    {
        if (Restart.NextCheck is not { } due)
        {
            return;
        }

        var wake = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        Interlocked.Exchange(ref _restartWake, wake)?.Cancel();
        var delay = due - TimeProvider.System.GetUtcNow();
        _ = WakeForRestartAsync(delay < MinRestartCheckDelay ? MinRestartCheckDelay : delay, wake.Token);
    }

    private async Task WakeForRestartAsync(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, ct);
            }

            Store.Invalidate(DataKind.Containers, SessionName);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed class HostRestartActions(WslcHost host) : IRestartActions
    {
        public Task StartAsync(string name, CancellationToken ct) => host.StartContainerCoreAsync(name);
    }

    public uint StartedCpuCount { get; private set; }

    public uint StartedMemoryMb { get; private set; }

    public async Task StartSessionAsync()
    {
        if (IsReady)
        {
            return;
        }

        var cpuCount = Settings.CpuCount;
        var memoryMb = Settings.MemoryMb;
        // Closing the app leaves the session (and its containers) running, and the API cannot reopen it.
        _attached = await SessionExistsAsync();
        if (!_attached)
        {
            await CreateSessionAsync(cpuCount, memoryMb);
        }

        StartedCpuCount = cpuCount;
        StartedMemoryMb = memoryMb;
        Store.Start();
    }

    private async Task<bool> SessionExistsAsync()
    {
        try
        {
            var sessions = await Cli.ListSessionsAsync();
            Store.SessionList.Seed(sessions);
            return sessions.Any(s => string.Equals(s.Name, SessionName, StringComparison.OrdinalIgnoreCase));
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private async Task CreateSessionAsync(uint cpuCount, uint memoryMb)
    {
        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(Path.Combine(_root, "Data"));
                var settings = new SessionSettings(SessionName, Path.Combine(_root, "Data"))
                {
                    CpuCount = cpuCount,
                    MemorySizeInMB = memoryMb,
                    EnableGpu = Settings.EnableGpu,
                };
                var session = new Session(settings);
                session.Start();
                session.ProcessCrashed += info => Store.Invalidate(DataKind.Containers, SessionName);
                session.Terminated += reason =>
                {
                    SessionEndedReason = reason.ToString();
                    Store.Invalidate(DataKind.Containers, SessionName);
                };
                _session = session;
                UseOwnedSession(session);
            });
        }
        catch (Exception)
        {
            // Another instance or a race may have created it first; otherwise surface the original failure.
            if (!await SessionExistsAsync())
            {
                throw;
            }

            _attached = true;
        }
    }

    /// <summary>The background data layer every page reads from.</summary>
    public WslcStore Store { get; }

    public ComposeRunner CreateComposeRunner(Action<string> log) =>
        new(
            async args =>
            {
                TrackComposeCommand(args);
                try
                {
                    return await WslcProcess.Default.RunAsync(new WslcInvocation((string[])["--session", SessionName, .. args]));
                }
                finally
                {
                    AfterComposeCommand(args);
                }
            },
            line => log(SecretService.Default.Redact(line)),
            async (args, env) =>
            {
                TrackComposeCommand(args);
                try
                {
                    return await WslcProcess.Default.RunAsync(new WslcInvocation((string[])["--session", SessionName, .. args], env));
                }
                finally
                {
                    AfterComposeCommand(args);
                }
            },
            SecretService.Default.Get);

    private static readonly HashSet<string> ReadOnlyComposeVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "inspect", "logs", "list", "ls", "ps", "images", "stats", "events", "info", "version", "exec",
    };

    // Compose drives wslc.exe directly. Pulls emit no events, and without an event stream nothing else would be noticed.
    private void AfterComposeCommand(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || ReadOnlyComposeVerbs.Contains(args[0]))
        {
            return;
        }

        Store.Invalidate(DataKind.Images, SessionName);
        if (!Store.IsEventDriven(SessionName))
        {
            foreach (var kind in new[] { DataKind.Containers, DataKind.Inspect, DataKind.Volumes, DataKind.Networks })
            {
                Store.Invalidate(kind, SessionName);
            }
        }
    }

    public SecretService Secrets => SecretService.Default;

    private void TrackComposeCommand(IReadOnlyList<string> args)
    {
        if (args.Count < 2)
        {
            return;
        }

        var target = args[^1];
        switch (args[0])
        {
            case "stop": Restart.NotifyUserStopped(target); break;
            case "start": Restart.NotifyUserStarted(target); break;
            case "rm": Restart.NotifyUserStopped(target); Restart.NotifyRemoved(target); break;
        }
    }

    public WslcCli Cli { get; } = new(WslcProcess.Default);

    public static readonly TimeSpan SessionDisposeBudget = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Releases this app's handles without touching the session or its containers: closing the GUI must leave
    /// containers running. Polling and app-started wslc children are cancelled, and releasing the native handle
    /// is bounded so the process can always exit.
    /// </summary>
    public void Shutdown()
    {
        _lifetime.Cancel();
        Store.Stop();
        ChildProcessTracker.KillAll();

        var session = _session;
        _session = null;
        if (session is null)
        {
            return;
        }

        BoundedWait.Run(() => Task.Run(() =>
        {
            try
            {
                _sdk?.ReleaseWatchers();
                session.Dispose();
            }
            catch
            {
                // Closing the window must not fail.
            }
        }), SessionDisposeBudget);
    }

    private StoreSources CreateSources() => new(
        ListSessions: async ct =>
        {
            var rows = await Cli.ListSessionsAsync();
            await Task.Run(() => ResolveCreators(rows), ct);
            return rows;
        },
        // The SDK can't enumerate containers; wslc.exe can, for every session.
        ListContainers: (s, ct) => Cli.ListContainersAsync(s, ct),
        ListImages: (s, _) => _router.For(Handle(s)).ListImagesAsync(),
        ListResources: (s, kind, _) => _router.For(Handle(s)).ListResourceRowsAsync(kind),
        InspectContainers: (s, names, _) => _router.For(Handle(s)).InspectContainersAsync(names),
        Versions: async _ =>
        {
            var (wsl, kernel, manager) = await Cli.GetVersionsAsync();
            return new VersionInfo(wsl, kernel, manager);
        },
        Stats: (s, containers, ct) => Cli.GetStatsAsync(s, containers, ct));

    private static bool IsRunningState(string state) => state.Equals("running", StringComparison.OrdinalIgnoreCase);

    public bool IsAppSession(string session) => string.Equals(session, SessionName, StringComparison.OrdinalIgnoreCase);

    public Task<ImageDetail> InspectImageAsync(string session, string target) => Cli.InspectImageAsync(session, target);

    /// <summary>Versions and per-session counts for the Sessions page, from what the store already holds.</summary>
    public EnvironmentInfo GetEnvironment()
    {
        var versions = Store.Versions.Latest;
        var containers = Store.Containers();
        var images = Store.Images();
        var items = (Store.SessionList.Latest ?? []).Select(s =>
        {
            var c = Store.HasLoaded(DataKind.Containers, s.Name)
                ? Counts(containers.Items.Where(e => IsSession(e.Session, s.Name)).Select(e => e.Item).ToList())
                : "–";
            var i = Store.HasLoaded(DataKind.Images, s.Name)
                ? images.Images.Count(e => IsSession(e.Session, s.Name)).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "–";
            return new SessionInfo(s.Id, s.Name, CreatorName(s.CreatorPid), s.CreatorPid, c, i, IsAppSession(s.Name));
        }).ToList();

        return new EnvironmentInfo(versions?.Wsl ?? string.Empty, versions?.Kernel ?? string.Empty, versions?.SessionManager ?? string.Empty, items);

        static bool IsSession(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        static string Counts(IReadOnlyList<ContainerRow> list) => $"{list.Count(x => IsRunningState(x.State))} / {list.Count}";
    }

    private readonly Dictionary<string, string> _creatorNames = new(StringComparer.Ordinal);

    private string CreatorName(string pid)
    {
        lock (_creatorNames)
        {
            return _creatorNames.GetValueOrDefault(pid) ?? "Exited";
        }
    }

    // Runs with each session list read, off the UI thread. Process.ProcessName snapshots every process on the machine,
    // so a live creator's name is read once and reused; only liveness is checked each time.
    private void ResolveCreators(IReadOnlyList<SessionRow> rows)
    {
        var live = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pid in rows.Select(r => r.CreatorPid).Distinct())
        {
            if (!int.TryParse(pid, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var id))
            {
                continue;
            }

            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(id);
                string? known;
                lock (_creatorNames)
                {
                    known = _creatorNames.GetValueOrDefault(pid);
                }

                live[pid] = known ?? process.ProcessName;
            }
            catch
            {
                // Exited.
            }
        }

        lock (_creatorNames)
        {
            _creatorNames.Clear();
            foreach (var (pid, name) in live)
            {
                _creatorNames[pid] = name;
            }
        }
    }

    public async Task TerminateSessionAsync(string session)
    {
        try
        {
            await Cli.TerminateSessionAsync(session);
        }
        finally
        {
            await Store.RefreshAsync(DataKind.Sessions);
        }
    }

    public string SessionId => SessionName;

    // Used by the restart engine, which must not look like a user start.
    private Task StartContainerCoreAsync(string name) => _router.For(AppSession).StartContainerAsync(name);


    public void SetRestartPolicy(string name, RestartPolicy policy, bool running) =>
        Restart.SetPolicy(name, policy, running);

    public SessionHandle AppSession => new(SessionName, IsThisApp: true);

    public SessionHandle Handle(string? session) => new(session ?? SessionName, IsAppSession(session ?? SessionName));

    private static ISessionBackend CliBackend(string session) => new WslcCliBackend(WslcProcess.Default, session, SecretService.Default.Get);

    private BackendRouter _router;

    /// <summary>Everything the UI can do to a WSLc session, routed to the SDK or wslc.exe by session.</summary>
    public WslcOperations Operations { get; private set; }

    private SdkSessionBackend? _sdk;

    private void UseOwnedSession(Session session)
    {
        var sdk = new SdkSessionBackend(session, CliBackend(SessionName));
        sdk.ContainersChanged += () => Store.Invalidate(DataKind.Containers, SessionName);
        _sdk = sdk;
        _router = new BackendRouter(sdk, CliBackend);
        Operations = new WslcOperations(_router, new HostEffects(this));
        Store.NativeAppEvents = true;
    }

    private sealed class HostEffects(WslcHost host) : IOperationEffects
    {
        public void ImagesChanged(string session) => host.Store.Invalidate(DataKind.Images, session);

        public void ContainersChanged(string session)
        {
            host.Store.Invalidate(DataKind.Containers, session);
            host.Store.Invalidate(DataKind.Inspect, session);
            // Image rows count the containers using them, and creating a container can create volumes.
            host.Store.Invalidate(DataKind.Images, session);
            host.Store.Invalidate(DataKind.Volumes, session);
        }

        public void ResourcesChanged(ResourceKind kind, string session) =>
            host.Store.Invalidate(kind == ResourceKind.Volume ? DataKind.Volumes : DataKind.Networks, session);

        public void UserStarted(string container) => host.Restart.NotifyUserStarted(container);

        public void UserStopped(string container) => host.Restart.NotifyUserStopped(container);

        public void Removed(string container) => host.Restart.NotifyRemoved(container);
    }


    public static string FormatSize(ulong bytes) => SizeFormat.Format(bytes);
}
