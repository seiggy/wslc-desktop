using WslcDesktop.Resources;
using WslcDesktop.Sessions;

namespace WslcDesktop.Backends;

/// <summary>Volumes and networks.</summary>
public sealed class ResourceOperations(BackendRouter router, IOperationEffects effects, Func<string, string> redact)
{
    public Task<IReadOnlyList<ResourceRow>> ListAsync(SessionHandle session, ResourceKind kind) => router.For(session).ListResourcesAsync(kind);

    public Task<IReadOnlyList<string>> NetworkNamesAsync(SessionHandle session) => router.For(session).ListNetworkNamesAsync();

    public async Task<string> InspectAsync(SessionHandle session, ResourceKind kind, string name) =>
        redact(await router.For(session).InspectResourceAsync(kind, name));

    public async Task CreateVolumeAsync(SessionHandle session, string name, string driver, long? sizeBytes = null)
    {
        await router.For(session).CreateVolumeAsync(name, driver, sizeBytes);
        effects.ResourcesChanged(ResourceKind.Volume, session.Name);
    }

    public async Task CreateNetworkAsync(SessionHandle session, string name, string driver, string subnet, string gateway, bool isInternal)
    {
        await router.For(session).CreateNetworkAsync(name, driver, subnet, gateway, isInternal);
        effects.ResourcesChanged(ResourceKind.Network, session.Name);
    }

    public async Task RemoveAsync(SessionHandle session, ResourceKind kind, string name)
    {
        await router.For(session).RemoveResourceAsync(kind, name);
        effects.ResourcesChanged(kind, session.Name);
    }

    public async Task PruneAsync(SessionHandle session, ResourceKind kind)
    {
        await router.For(session).PruneResourcesAsync(kind);
        effects.ResourcesChanged(kind, session.Name);
    }

    public async Task ConnectNetworkAsync(SessionHandle session, string network, string container, string alias)
    {
        await router.For(session).ConnectNetworkAsync(network, container, alias);
        NetworkMembershipChanged(session);
    }

    public async Task DisconnectNetworkAsync(SessionHandle session, string network, string container)
    {
        await router.For(session).DisconnectNetworkAsync(network, container);
        NetworkMembershipChanged(session);
    }

    private void NetworkMembershipChanged(SessionHandle session)
    {
        effects.ResourcesChanged(ResourceKind.Network, session.Name);
        effects.ContainersChanged(session.Name);
    }
}
