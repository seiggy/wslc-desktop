using Microsoft.WSL.Containers;

namespace WslcDesktop.Services;

public sealed record ContainerRow(string Name, string Image, string Id, string State, string Command, string Ports, string Labels = "");

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

    public bool IsReady => _session is not null;

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

    public uint StartedCpuCount { get; private set; }

    public uint StartedMemoryMb { get; private set; }

    public async Task StartSessionAsync()
    {
        if (_session is not null)
        {
            return;
        }

        var cpuCount = Settings.CpuCount;
        var memoryMb = Settings.MemoryMb;
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
            _session = session;
        });
        StartedCpuCount = cpuCount;
        StartedMemoryMb = memoryMb;
        Telemetry.Start(SessionName);
        Monitor.Start(GetContainersAsync);
    }

    public ContainerMonitor Monitor { get; } = new();

    public Compose.ComposeRunner CreateComposeRunner(Action<string> log) =>
        new(args => WslcCli.RunAsync(["--session", SessionName, .. args]), log);

    public TelemetryService Telemetry { get; } = new();

    public Task StreamLogsAsync(string container, int tail, bool follow, bool timestamps, Action<string> onLine, CancellationToken ct) =>
        WslcCli.StreamLogsAsync(SessionName, container, tail, follow, timestamps, onLine, ct);

    public ShellProcess StartShell(string container, string shell, Action<string> onOutput, Action<int> onExit) =>
        WslcCli.StartShell(SessionName, container, shell, onOutput, onExit);

    public void Shutdown()
    {
        Telemetry.Stop();
        Monitor.Stop();
        try
        {
            _session?.Terminate();
            _session?.Dispose();
        }
        catch
        {
            // Closing the window must not fail on a session that already ended.
        }

        _session = null;
    }

    public Task<IReadOnlyList<ImageRow>> GetImagesAsync() => Run(s =>
        (IReadOnlyList<ImageRow>)s.GetImages()
            .Select(i => new ImageRow(i.Name, FormatSize(i.Size), i.CreatedTimestamp))
            .ToList());

    public async Task PullImageAsync(string uri, IProgress<ImageProgress> progress)
    {
        var session = Require();
        var op = session.PullImageAsync(new PullImageOptions(uri));
        op.Progress = (_, p) => progress.Report(p);
        await op;
    }

    public Task<IReadOnlyList<ImageSummary>> GetImageSummariesAsync() => WslcCli.ListImagesAsync(SessionName);

    public Task<ImageDetail> InspectImageAsync(string target) => WslcCli.InspectImageAsync(SessionName, target);

    public Task DeleteImageAsync(string nameOrId) => Run(s => s.DeleteImage(nameOrId));

    // The API cannot enumerate containers; wslc.exe can.
    public Task<IReadOnlyList<ContainerRow>> GetContainersAsync() => WslcCli.ListContainersAsync(SessionName);

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

    public Task CreateContainerAsync(string name, string image, string[] commandLine)
    {
        return Run(s =>
        {
            var init = new ProcessSettings { OutputMode = ProcessOutputMode.Discard };
            if (commandLine.Length > 0)
            {
                init.CommandLine = commandLine;
            }

            using var c = s.CreateContainer(new ContainerSettings(image) { Name = name, InitProcess = init });
        });
    }

    public Task StartContainerAsync(string name) => Run(s =>
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

    public Task StopContainerAsync(string name) =>
        WithContainer(name, c => c.Stop(Signal.SIGTERM, TimeSpan.FromSeconds(10)));

    public Task DeleteContainerAsync(string name) => WithContainer(name, c =>
    {
        c.Delete(c.State == ContainerState.Running ? DeleteContainerOption.Force : DeleteContainerOption.None);
    });

    public Task<string> InspectContainerAsync(string name)
    {
        var s = Require();
        return Task.Run(() =>
        {
            using var c = s.OpenContainer(name, ProcessOutputMode.Discard);
            return c.Inspect();
        });
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
