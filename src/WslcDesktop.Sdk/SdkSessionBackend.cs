using Microsoft.WSL.Containers;
using WslcDesktop.Backends;
using WslcDesktop.Cli;
using WslcDesktop.Containers;
using WslcDesktop.Containers.Creation;
using WslcDesktop.Images;
using WslcDesktop.Resources;
using WslcDesktop.Secrets;

namespace WslcDesktop.Sdk;

/// <summary>
/// Drives a session this process owns through the in-process SDK. The SDK cannot enumerate or manage volumes,
/// networks, registries or images listings, so those go through the wslc.exe fallback.
/// </summary>
public sealed class SdkSessionBackend(Session session, ISessionBackend fallback) : ISessionBackend, IDisposable
{
    public string Session { get; } = fallback.Session;

    public static SdkSessionBackend Create(string name, string dataDirectory, ISessionBackend fallback)
    {
        Directory.CreateDirectory(dataDirectory);
        var s = new Session(new SessionSettings(name, dataDirectory));
        s.Start();
        return new SdkSessionBackend(s, fallback);
    }

    public void Dispose() => session.Dispose();

    private Task Run(Action<Session> work) => Task.Run(() => work(session));

    private Task WithContainer(string name, Action<Container> action) => Run(s =>
    {
        using var c = s.OpenContainer(name, ProcessOutputMode.Discard);
        action(c);
    });

    // Native operations

    public async Task PullImageAsync(string uri, IProgress<PullProgress>? progress)
    {
        var op = session.PullImageAsync(new PullImageOptions(uri));
        op.Progress = (_, p) => progress?.Report(new PullProgress(p.Status.ToString(), p.Id, (long)p.CurrentBytes, (long)p.TotalBytes));
        await op;
    }

    public Task DeleteImageAsync(string target) => Run(s => s.DeleteImage(target));

    public Task CreateContainerAsync(string name, string image, string[] commandLine, IReadOnlyDictionary<string, string> environment) => Run(s =>
    {
        var init = new ProcessSettings { OutputMode = ProcessOutputMode.Discard };
        if (commandLine.Length > 0)
        {
            init.CommandLine = commandLine;
        }

        if (environment.Count > 0)
        {
            init.EnvironmentVariables = environment.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        }

        using var c = s.CreateContainer(new ContainerSettings(image) { Name = name, InitProcess = init });
    });

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

    public Task StopContainerAsync(string name) => WithContainer(name, c => c.Stop(Signal.SIGTERM, TimeSpan.FromSeconds(10)));

    public Task DeleteContainerAsync(string name) => WithContainer(name, c =>
        c.Delete(c.State == ContainerState.Running ? DeleteContainerOption.Force : DeleteContainerOption.None));

    public Task<string> InspectContainerAsync(string name) => Task.Run(() =>
    {
        using var c = session.OpenContainer(name, ProcessOutputMode.Discard);
        return c.Inspect();
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

    // Everything else goes through wslc.exe

    public Task<IReadOnlyList<ContainerRow>> ListContainersAsync() => fallback.ListContainersAsync();

    public Task StreamLogsAsync(string container, LogOptions options, Action<string> onLine, CancellationToken ct) =>
        fallback.StreamLogsAsync(container, options, onLine, ct);

    public IShellProcess StartShell(string container, string shell, Action<string> onOutput, Action<int> onExit) =>
        fallback.StartShell(container, shell, onOutput, onExit);

    public Task CreateContainerAsync(NewContainerSpec spec) => fallback.CreateContainerAsync(spec);

    public Task<IReadOnlyList<ImageSummary>> ListImagesAsync() => fallback.ListImagesAsync();

    public Task<IReadOnlyList<ResourceRow>> ListResourcesAsync(ResourceKind kind) => fallback.ListResourcesAsync(kind);

    public Task<string> InspectResourceAsync(ResourceKind kind, string name) => fallback.InspectResourceAsync(kind, name);

    public Task CreateVolumeAsync(string name, string driver, long? sizeBytes) => fallback.CreateVolumeAsync(name, driver, sizeBytes);

    public Task CreateNetworkAsync(string name, string driver, string subnet, string gateway, bool isInternal) =>
        fallback.CreateNetworkAsync(name, driver, subnet, gateway, isInternal);

    public Task RemoveResourceAsync(ResourceKind kind, string name) => fallback.RemoveResourceAsync(kind, name);

    public Task PruneResourcesAsync(ResourceKind kind) => fallback.PruneResourcesAsync(kind);

    public Task ConnectNetworkAsync(string network, string container, string alias) => fallback.ConnectNetworkAsync(network, container, alias);

    public Task DisconnectNetworkAsync(string network, string container) => fallback.DisconnectNetworkAsync(network, container);

    public Task RegistryLoginAsync(string server, string username, string password) => fallback.RegistryLoginAsync(server, username, password);

    public Task RegistryLogoutAsync(string server) => fallback.RegistryLogoutAsync(server);
}
