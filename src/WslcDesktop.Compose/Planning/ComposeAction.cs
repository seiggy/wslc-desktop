using WslcDesktop.Compose.Planning.Services;
namespace WslcDesktop.Compose.Planning;

/// <summary>One planned wslc invocation. <see cref="Args"/> excludes the --session option.</summary>
public sealed record ComposeAction(
    ActionKind Kind,
    string Target,
    string Description,
    IReadOnlyList<string> Args,
    string? Service = null,
    IReadOnlyList<ServiceWait>? Waits = null,
    IReadOnlyList<IReadOnlyList<string>>? After = null,
    string? Restart = null);
