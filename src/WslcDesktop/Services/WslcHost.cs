using Microsoft.WSL.Containers;

namespace WslcDesktop.Services;

public sealed record ContainerRow(string Name, string Image, string Id, string State, string Command, string Ports);

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

    public async Task StartSessionAsync(uint cpuCount = 4, uint memoryMb = 4096)
    {
        if (_session is not null)
        {
            return;
        }

        await Task.Run(() =>
        {
            Directory.CreateDirectory(Path.Combine(_root, "Data"));
            var settings = new SessionSettings(SessionName, Path.Combine(_root, "Data"))
            {
                CpuCount = cpuCount,
                MemorySizeInMB = memoryMb,
            };
            var session = new Session(settings);
            session.Start();
            _session = session;
        });
    }

    public void Shutdown()
    {
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

    public Task DeleteImageAsync(string nameOrId) => Run(s => s.DeleteImage(nameOrId));

    // The API cannot enumerate containers; wslc.exe can.
    public Task<IReadOnlyList<ContainerRow>> GetContainersAsync() => WslcCli.ListContainersAsync(SessionName);

    public Task<IReadOnlyList<SessionRow>> GetSessionsAsync() => WslcCli.ListSessionsAsync();

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

    public Task StartContainerAsync(string name) => WithContainer(name, c => c.Start());

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
