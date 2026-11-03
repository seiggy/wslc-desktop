using Microsoft.WSL.Containers;

namespace WslcDesktop.Services;

public sealed record ContainerRow(string Name, string Image, string Id, string State, string Command, string Ports, string Labels = "", string Status = "");

public sealed record ImageRow(string Name, string Size, DateTimeOffset Created);

/// <summary>
/// Owns the single WSLc session and wraps the synchronous Microsoft.WSL.Containers API
/// so every call runs off the UI thread.
/// </summary>
public sealed class WslcHost
{
    private const string SessionName = "WslcDesktop";

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
        Telemetry.Start(SessionName);
        Monitor.Start(ct => GetContainersAsync(ct));
    }

    private async Task<bool> SessionExistsAsync()
    {
        try
        {
            var sessions = await WslcCli.ListSessionsAsync();
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

    public Compose.ComposeRunner CreateComposeRunner(Action<string> log) =>
        new(
            args =>
            {
                TrackComposeCommand(args);
                return WslcCli.RunAsync(["--session", SessionName, .. args]);
            },
            line => log(SecretService.Default.Redact(line)),
            (args, env) =>
            {
                TrackComposeCommand(args);
                return WslcCli.RunWithEnvAsync(env, ["--session", SessionName, .. args]);
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

    public TelemetryService Telemetry { get; } = new();

    public Task StreamLogsAsync(string container, int tail, bool follow, bool timestamps, Action<string> onLine, CancellationToken ct) =>
        WslcCli.StreamLogsAsync(SessionName, container, tail, follow, timestamps, onLine, ct);

    public ShellProcess StartShell(string container, string shell, Action<string> onOutput, Action<int> onExit) =>
        WslcCli.StartShell(SessionName, container, shell, onOutput, onExit);

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
    public async Task<IReadOnlyList<ImageRow>> GetImagesAsync()
    {
        if (_attached)
        {
            var images = await WslcCli.ListImagesAsync(SessionName);
            return images.Select(i => new ImageRow($"{i.Repository}:{i.Tag}", i.Size, DateTimeOffset.MinValue)).ToList();
        }

        return await Run(s => (IReadOnlyList<ImageRow>)s.GetImages()
            .Select(i => new ImageRow(i.Name, FormatSize(i.Size), i.CreatedTimestamp))
            .ToList());
    }

    public async Task PullImageAsync(string uri, IProgress<ImageProgress> progress)
    {
        if (_attached)
        {
            await WslcCli.RunAsync("--session", SessionName, "pull", uri);
            return;
        }

        var session = Require();
        var op = session.PullImageAsync(new PullImageOptions(uri));
        op.Progress = (_, p) => progress.Report(p);
        await op;
    }

    public Task<IReadOnlyList<ImageSummary>> GetImageSummariesAsync() => WslcCli.ListImagesAsync(SessionName);

    public Task<ImageDetail> InspectImageAsync(string target) => WslcCli.InspectImageAsync(SessionName, target);

    public Task DeleteImageAsync(string nameOrId) => _attached
        ? WslcCli.RunAsync("--session", SessionName, "rmi", nameOrId)
        : Run(s => s.DeleteImage(nameOrId));

    // The API cannot enumerate containers; wslc.exe can.
    public Task<IReadOnlyList<ContainerRow>> GetContainersAsync(CancellationToken ct = default) => WslcCli.ListContainersAsync(SessionName, ct);

    public Task<IReadOnlyList<SessionRow>> GetSessionsAsync() => WslcCli.ListSessionsAsync();

    public Task<EnvironmentInfo> GetEnvironmentAsync() => WslcCli.GetEnvironmentAsync(SessionName);

    public Task TerminateSessionAsync(string session) => WslcCli.TerminateSessionAsync(session);

    public string SessionId => SessionName;

    public Task<IReadOnlyList<ResourceRow>> GetResourcesAsync(ResourceKind kind) =>
        kind == ResourceKind.Volume ? WslcResources.ListVolumesAsync(SessionName) : WslcResources.ListNetworksAsync(SessionName);

    public Task<string> InspectResourceAsync(ResourceKind kind, string name) => WslcResources.InspectAsync(SessionName, kind, name);

    public Task CreateVolumeAsync(string name, string driver) => WslcResources.CreateVolumeAsync(SessionName, name, driver);

    public Task CreateNetworkAsync(string name, string driver, string subnet, string gateway, bool isInternal) =>
        WslcResources.CreateNetworkAsync(SessionName, name, driver, subnet, gateway, isInternal);

    public Task RemoveResourceAsync(ResourceKind kind, string name) => WslcResources.RemoveAsync(SessionName, kind, name);

    public Task RegistryLoginAsync(string server, string username, string password) =>
        WslcRegistry.LoginAsync(SessionName, server, username, password);

    public Task RegistryLogoutAsync(string server) => WslcRegistry.LogoutAsync(SessionName, server);

    public Task PruneResourcesAsync(ResourceKind kind) => WslcResources.PruneAsync(SessionName, kind);

    public Task<IReadOnlyList<string>> GetNetworkNamesAsync() => WslcResources.ListNetworkNamesAsync(SessionName);

    public Task ConnectNetworkAsync(string network, string container, string alias) =>
        WslcResources.ConnectAsync(SessionName, network, container, alias);

    public Task DisconnectNetworkAsync(string network, string container) =>
        WslcResources.DisconnectAsync(SessionName, network, container);

    public Task CreateContainerAsync(NewContainerSpec spec) => WslcResources.CreateContainerAsync(SessionName, spec);

    /// <summary>
    /// Creates a container from a name, image and command. <paramref name="env"/> may contain <c>${secret:NAME}</c> tokens in
    /// its values: in an owned session they go through <c>ProcessSettings.EnvironmentVariables</c>; when attached they are
    /// passed to wslc as inherited <c>--env NAME</c> variables so values never reach a command line.
    /// </summary>
    public Task CreateContainerAsync(string name, string image, string[] commandLine, IReadOnlyDictionary<string, string>? env = null)
    {
        var secrets = SecretService.Default;
        var resolved = (env ?? new Dictionary<string, string>())
            .ToDictionary(kv => kv.Key, kv => Compose.SecretTokens.Resolve(kv.Value, secrets.Get), StringComparer.Ordinal);
        if (_attached)
        {
            var args = new List<string> { "--session", SessionName, "create", "--name", name };
            foreach (var key in resolved.Keys)
            {
                args.AddRange(["--env", key]);
            }

            args.Add(image);
            args.AddRange(commandLine);
            return WslcCli.RunWithEnvAsync(resolved, args);
        }

        return Run(s =>
        {
            var init = new ProcessSettings { OutputMode = ProcessOutputMode.Discard };
            if (commandLine.Length > 0)
            {
                init.CommandLine = commandLine;
            }

            if (resolved.Count > 0)
            {
                init.EnvironmentVariables = resolved;
            }

            using var c = s.CreateContainer(new ContainerSettings(image) { Name = name, InitProcess = init });
        });
    }

    public Task StartContainerAsync(string name)
    {
        Restart.NotifyUserStarted(name);
        return StartContainerCoreAsync(name);
    }

    // Also used by the restart engine, which must not look like a user start.
    private Task StartContainerCoreAsync(string name) => _attached
        ? WslcCli.RunAsync("--session", SessionName, "start", name)
        : Run(s =>
    {
        using var c = s.OpenContainer(name, ProcessOutputMode.Discard);
        try
        {
            c.Start();
        }
        catch (Exception) when (IsRunning(s, name))
        {
            // Containers made with `create` can start but fail to attach the init process handle.
        }
    });

    private static bool IsRunning(Session s, string name)
    {
        try
        {
            using var c = s.OpenContainer(name, ProcessOutputMode.Discard);
            return c.State == ContainerState.Running;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public Task StopContainerAsync(string name)
    {
        Restart.NotifyUserStopped(name);
        return _attached
            ? WslcCli.RunAsync("--session", SessionName, "stop", name)
            : WithContainer(name, c => c.Stop(Signal.SIGTERM, TimeSpan.FromSeconds(10)));
    }

    public async Task DeleteContainerAsync(string name)
    {
        Restart.NotifyUserStopped(name);
        await (_attached
            ? WslcCli.RunAsync("--session", SessionName, "rm", "--force", name)
            : WithContainer(name, c =>
            {
                c.Delete(c.State == ContainerState.Running ? DeleteContainerOption.Force : DeleteContainerOption.None);
            }));
        Restart.NotifyRemoved(name);
    }

    public void SetRestartPolicy(string name, RestartPolicy policy, bool running) =>
        Restart.SetPolicy(name, policy, running);

    public async Task<string> InspectContainerAsync(string name)
    {
        if (_attached)
        {
            return SecretService.Default.Redact(await WslcCli.RunAsync("--session", SessionName, "inspect", name));
        }

        var s = Require();
        var json = await Task.Run(() =>
        {
            using var c = s.OpenContainer(name, ProcessOutputMode.Discard);
            return c.Inspect();
        });
        return SecretService.Default.Redact(json);
    }

    private Task WithContainer(string name, Action<Container> action) => Run(s =>
    {
        using var c = s.OpenContainer(name, ProcessOutputMode.Discard);
        action(c);
    });

    private Task Run(Action<Session> work)
    {
        var s = Require();
        return Task.Run(() => work(s));
    }

    private Task<T> Run<T>(Func<Session, T> work)
    {
        var s = Require();
        return Task.Run(() => work(s));
    }

    private Session Require() =>
        _session ?? throw new InvalidOperationException("The WSLc session is not running.");

    public static string FormatSize(ulong bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}
