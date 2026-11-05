using WslcDesktop.Backends;
using WslcDesktop.Cli;
using WslcDesktop.Containers;
using WslcDesktop.Containers.Creation;
using WslcDesktop.Images;
using WslcDesktop.Resources;

namespace WslcDesktop.Tests.Backends;

/// <summary>An in-memory adapter for <see cref="ISessionBackend"/>: records calls and keeps simple state.</summary>
public sealed class InMemoryBackend(string session) : ISessionBackend
{
    public string Session { get; } = session;

    public List<string> Calls { get; } = [];

    public Exception? FailWith { get; set; }

    public List<ContainerRow> Containers { get; } = [];

    public List<ImageSummary> Images { get; } = [];

    public List<string> LogLines { get; } = [];

    public FakeShell Shell { get; } = new();

    private Task Record(string call)
    {
        if (FailWith is not null)
        {
            throw FailWith;
        }

        Calls.Add(call);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ContainerRow>> ListContainersAsync() => Task.FromResult<IReadOnlyList<ContainerRow>>(Containers.ToList());

    public Task CreateContainerAsync(NewContainerSpec spec) => Record($"create {spec.Name}");

    public Task CreateContainerAsync(string name, string image, string[] commandLine, IReadOnlyDictionary<string, string> environment) =>
        Record($"create {name} env={string.Join(",", environment.Keys)}");

    public Task StreamLogsAsync(string container, LogOptions options, Action<string> onLine, CancellationToken ct)
    {
        var call = Record($"logs {container} tail={options.Tail} follow={options.Follow} ts={options.Timestamps}");
        foreach (var line in LogLines)
        {
            onLine(line);
        }

        return call;
    }

    public IShellProcess StartShell(string container, string shell, Action<string> onOutput, Action<int> onExit)
    {
        _ = Record($"shell {container} {shell}");
        return Shell.Attach(onOutput, onExit);
    }

    public Task StartContainerAsync(string name) => Record($"start {name}");

    public Task StopContainerAsync(string name) => Record($"stop {name}");

    public Task DeleteContainerAsync(string name) => Record($"rm {name}");

    public async Task<string> InspectContainerAsync(string name)
    {
        await Record($"inspect {name}");
        return $"{{\"Name\":\"{name}\"}}";
    }

    public Task<IReadOnlyList<ImageSummary>> ListImagesAsync() => Task.FromResult<IReadOnlyList<ImageSummary>>(Images.ToList());

    public Task PullImageAsync(string uri, IProgress<PullProgress>? progress) => Record($"pull {uri}");

    public Task DeleteImageAsync(string target) => Record($"rmi {target}");

    public Task<IReadOnlyList<ResourceRow>> ListResourcesAsync(ResourceKind kind) => Task.FromResult<IReadOnlyList<ResourceRow>>([]);

    public Task<string> InspectResourceAsync(ResourceKind kind, string name) => Task.FromResult("{}");

    public Task CreateVolumeAsync(string name, string driver, long? sizeBytes) => Record($"volume create {name}");

    public Task CreateNetworkAsync(string name, string driver, string subnet, string gateway, bool isInternal) => Record($"network create {name}");

    public Task RemoveResourceAsync(ResourceKind kind, string name) => Record($"{kind} remove {name}");

    public Task PruneResourcesAsync(ResourceKind kind) => Record($"{kind} prune");

    public Task ConnectNetworkAsync(string network, string container, string alias) => Record($"connect {network} {container}");

    public Task DisconnectNetworkAsync(string network, string container) => Record($"disconnect {network} {container}");

    public Task RegistryLoginAsync(string server, string username, string password) => Record($"login {server}");

    public Task RegistryLogoutAsync(string server) => Record($"logout {server}");
}
