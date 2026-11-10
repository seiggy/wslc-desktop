using Microsoft.WSL.Containers;
using WslcDesktop.Backends;
using WslcDesktop.Cli;
using WslcDesktop.Sdk;
using WslcDesktop.Common;
using WslcDesktop.Compose.Execution;
using WslcDesktop.Compose.Secrets.Injection;
using WslcDesktop.Containers;
using WslcDesktop.Containers.Creation;
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
        Telemetry = new TelemetryService(Cli, ListSessionNamesAsync);
        _router = new BackendRouter(owned: null, CliBackend);
        Operations = new WslcOperations(_router, new HostEffects(this));
        Restart = new RestartEngine(new RestartPolicyStore(), new HostRestartActions(this), TimeProvider.System);
        Monitor.Updated += OnMonitorUpdated;
    }

    /// <summary>App-level restart policies. They only run while this app is running.</summary>
    public RestartEngine Restart { get; }

    /// <summary>Why the WSLc session ended, when this app owns it and the API reported it (attached mode can't see this).</summary>
    public string? SessionEndedReason { get; private set; }

    private void OnMonitorUpdated()
    {
        if (Monitor.LastError is not null || !Monitor.HasLoaded)
        {
            return;
        }

        var observations = Monitor.Containers
            .Select(c => new ContainerObservation(c.Name, c.State, ContainerStatusText.ExitCode(c.Status)))
            .ToList();
        _ = ProcessRestartsAsync(observations);
    }

    private async Task ProcessRestartsAsync(IReadOnlyList<ContainerObservation> observations)
    {
        try
        {
            await Restart.ProcessAsync(observations, _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // A failed restart pass is retried on the next snapshot.
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
        Telemetry.Start();
        Monitor.Start(ct => GetContainersAsync(ct));
    }

    private async Task<bool> SessionExistsAsync()
    {
        try
        {
            var sessions = await Cli.ListSessionsAsync();
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
                session.ProcessCrashed += info => { _ = Monitor.RefreshAsync(); };
                session.Terminated += reason =>
                {
                    SessionEndedReason = reason.ToString();
                    _ = Monitor.RefreshAsync();
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

    public ContainerMonitor Monitor { get; } = new();

    public ComposeRunner CreateComposeRunner(Action<string> log) =>
        new(
            args =>
            {
                TrackComposeCommand(args);
                return WslcProcess.Default.RunAsync(new WslcInvocation((string[])["--session", SessionName, .. args]));
            },
            line => log(SecretService.Default.Redact(line)),
            (args, env) =>
            {
                TrackComposeCommand(args);
                return WslcProcess.Default.RunAsync(new WslcInvocation((string[])["--session", SessionName, .. args], env));
            },
            SecretService.Default.Get);

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

    public TelemetryService Telemetry { get; }

    public static readonly TimeSpan SessionDisposeBudget = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Releases this app's handles without touching the session or its containers: closing the GUI must leave
    /// containers running. Polling and app-started wslc children are cancelled, and releasing the native handle
    /// is bounded so the process can always exit.
    /// </summary>
    public void Shutdown()
    {
        _lifetime.Cancel();
        Telemetry.Stop();
        Monitor.Stop();
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
                session.Dispose();
            }
            catch
            {
                // Closing the window must not fail.
            }
        }), SessionDisposeBudget);
    }

    private ImageCatalog? _imageCatalog;

    /// <summary>Images from every visible WSLc session (image stores are per session), throttled and failure tolerant.</summary>
    public ImageCatalog ImageCatalog => _imageCatalog ??= new ImageCatalog(
        SessionName,
        ListSessionNamesAsync,
        s => Operations.Images.ListAsync(Handle(s)),
        TimeProvider.System,
        TimeSpan.FromSeconds(10));

    private async Task<IReadOnlyList<string>> ListSessionNamesAsync() =>
        (await Cli.ListSessionsAsync()).Select(s => s.Name).ToList();

    private SessionCatalog<ContainerRow>? _containerCatalog;

    /// <summary>
    /// Containers from every visible session. The app's session comes from the monitor (no extra wslc call); other
    /// sessions are read through wslc.exe, throttled.
    /// </summary>
    public SessionCatalog<ContainerRow> ContainerCatalog => _containerCatalog ??= new(
        SessionName,
        ListSessionNamesAsync,
        s => IsAppSession(s) ? Task.FromResult(Monitor.Containers) : Operations.Containers.ListAsync(Handle(s)),
        TimeProvider.System,
        TimeSpan.FromSeconds(5));

    private SessionCatalog<ResourceRow>? _volumeCatalog;
    private SessionCatalog<ResourceRow>? _networkCatalog;

    /// <summary>Volumes or networks from every visible session (both are per session).</summary>
    public SessionCatalog<ResourceRow> ResourceCatalog(ResourceKind kind) => kind == ResourceKind.Volume
        ? _volumeCatalog ??= new(SessionName, ListSessionNamesAsync, s => Operations.Resources.ListAsync(Handle(s), ResourceKind.Volume), TimeProvider.System, TimeSpan.FromSeconds(10))
        : _networkCatalog ??= new(SessionName, ListSessionNamesAsync, s => Operations.Resources.ListAsync(Handle(s), ResourceKind.Network), TimeProvider.System, TimeSpan.FromSeconds(10));

    public bool IsAppSession(string session) => string.Equals(session, SessionName, StringComparison.OrdinalIgnoreCase);

    public Task<ImageDetail> InspectImageAsync(string session, string target) => Cli.InspectImageAsync(session, target);


    // The API cannot enumerate containers; wslc.exe can.
    public Task<IReadOnlyList<ContainerRow>> GetContainersAsync(CancellationToken ct = default) => Cli.ListContainersAsync(SessionName, ct);

    public Task<IReadOnlyList<SessionRow>> GetSessionsAsync() => Cli.ListSessionsAsync();

    public Task<EnvironmentInfo> GetEnvironmentAsync() => Cli.GetEnvironmentAsync(SessionName);

    public Task TerminateSessionAsync(string session) => Cli.TerminateSessionAsync(session);

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

    private void UseOwnedSession(Session session)
    {
        _router = new BackendRouter(new SdkSessionBackend(session, CliBackend(SessionName)), CliBackend);
        Operations = new WslcOperations(_router, new HostEffects(this));
    }

    private sealed class HostEffects(WslcHost host) : IOperationEffects
    {
        public void ImagesChanged(string session) => host.ImageCatalog.Invalidate(session);

        public void ContainersChanged(string session) => host.ContainerCatalog.Invalidate(session);

        public void ResourcesChanged(ResourceKind kind, string session) => host.ResourceCatalog(kind).Invalidate(session);

        public void UserStarted(string container) => host.Restart.NotifyUserStarted(container);

        public void UserStopped(string container) => host.Restart.NotifyUserStopped(container);

        public void Removed(string container) => host.Restart.NotifyRemoved(container);
    }


    public static string FormatSize(ulong bytes) => SizeFormat.Format(bytes);
}
