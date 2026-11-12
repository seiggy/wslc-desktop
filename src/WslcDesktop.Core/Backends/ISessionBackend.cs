using WslcDesktop.Cli;
using WslcDesktop.Containers;
using WslcDesktop.Containers.Creation;
using WslcDesktop.Images;
using WslcDesktop.Resources;

namespace WslcDesktop.Backends;

/// <summary>
/// The seam for operating one WSLc session. Adapters: the SDK for an app-owned session, wslc.exe for everything else,
/// and an in-memory fake in tests. Methods are primitive and name-based; routing, cache invalidation and restart
/// bookkeeping live in <see cref="WslcOperations"/>.
/// </summary>
public interface ISessionBackend
{
    string Session { get; }

    Task<IReadOnlyList<ContainerRow>> ListContainersAsync();

    Task CreateContainerAsync(NewContainerSpec spec);

    Task CreateContainerAsync(string name, string image, string[] commandLine, IReadOnlyDictionary<string, string> environment);

    Task StartContainerAsync(string name);

    Task StreamLogsAsync(string container, LogOptions options, Action<string> onLine, CancellationToken ct);

    IShellProcess StartShell(string container, string shell, Action<string> onOutput, Action<int> onExit);

    Task StopContainerAsync(string name);

    Task DeleteContainerAsync(string name);

    Task<string> InspectContainerAsync(string name);

    Task<IReadOnlyList<ImageSummary>> ListImagesAsync();

    Task PullImageAsync(string uri, IProgress<PullProgress>? progress);

    Task DeleteImageAsync(string target);

    Task<IReadOnlyList<ResourceRow>> ListResourcesAsync(ResourceKind kind);

    /// <summary>Network names only. Adapters override this when they can skip the per-container usage lookup.</summary>
    async Task<IReadOnlyList<string>> ListNetworkNamesAsync() =>
        (await ListResourcesAsync(ResourceKind.Network)).Select(r => r.Name).ToList();

    /// <summary>Volumes or networks without "used by" (the store derives that from its container inspections).</summary>
    Task<IReadOnlyList<ResourceRow>> ListResourceRowsAsync(ResourceKind kind) => ListResourcesAsync(kind);

    /// <summary>Raw inspect JSON for each named container that still exists. Adapters override this to batch.</summary>
    async Task<IReadOnlyDictionary<string, string>> InspectContainersAsync(IReadOnlyList<string> names)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            try
            {
                result[name] = await InspectContainerAsync(name);
            }
            catch (Exception)
            {
                // Removed since it was listed.
            }
        }

        return result;
    }

    Task<string> InspectResourceAsync(ResourceKind kind, string name);

    Task CreateVolumeAsync(string name, string driver, long? sizeBytes);

    Task CreateNetworkAsync(string name, string driver, string subnet, string gateway, bool isInternal);

    Task RemoveResourceAsync(ResourceKind kind, string name);

    Task PruneResourcesAsync(ResourceKind kind);

    Task ConnectNetworkAsync(string network, string container, string alias);

    Task DisconnectNetworkAsync(string network, string container);

    Task RegistryLoginAsync(string server, string username, string password);

    Task RegistryLogoutAsync(string server);
}
