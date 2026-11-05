using WslcDesktop.Compose.Secrets;

namespace WslcDesktop.Compose.Planning;

public sealed record ComposePlan(
    string Project,
    IReadOnlyList<ComposeAction> Actions,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    /// <summary>Stored secrets the plan references through <c>${secret:NAME}</c> tokens or compose <c>secrets:</c>.</summary>
    public IReadOnlyList<string> SecretRefs { get; init; } = [];

    /// <summary>Referenced secrets that the store doesn't have; the stack can't run until they're added.</summary>
    public IReadOnlyList<string> MissingSecrets { get; init; } = [];

    /// <summary>Every referenced secret with its users and stored state (a superset of <see cref="MissingSecrets"/>).</summary>
    public IReadOnlyList<SecretNeed> SecretNeeds { get; init; } = [];

    public bool CanRun => Errors.Count == 0 && Actions.Count > 0 && MissingSecrets.Count == 0;
}
