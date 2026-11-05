using WslcDesktop.Secrets;

namespace WslcDesktop.Backends;

/// <summary>
/// Everything the UI can do to a WSLc session, addressed by session handle. Routing, cache invalidation, restart
/// bookkeeping and secret redaction of anything read back from a container live here.
/// </summary>
public sealed class WslcOperations
{
    public WslcOperations(BackendRouter router, IOperationEffects effects, Func<string, string>? redact = null)
    {
        redact ??= SecretService.Default.Redact;
        Containers = new ContainerOperations(router, effects, redact);
        Images = new ImageOperations(router, effects);
        Resources = new ResourceOperations(router, effects, redact);
        Registry = new RegistryOperations(router);
    }

    public ContainerOperations Containers { get; }

    public ImageOperations Images { get; }

    public ResourceOperations Resources { get; }

    public RegistryOperations Registry { get; }
}
