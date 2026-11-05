using WslcDesktop.Compose.Planning.Resources;
using static WslcDesktop.Compose.Parsing.ComposeValues;

namespace WslcDesktop.Compose.Planning;

/// <summary>Shared inputs, resource references and diagnostics for one planning pass.</summary>
internal sealed class PlanContext(
    string project,
    ComposeOptions options,
    Dictionary<string, object?> services,
    List<string> warnings,
    List<string> errors)
{
    internal string Project { get; } = project;
    internal ComposeOptions Options { get; } = options;
    internal Dictionary<string, object?> Services { get; } = services;
    internal List<string> Warnings { get; } = warnings;
    internal List<string> Errors { get; } = errors;
    internal Dictionary<string, NetworkRef> Networks { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, VolumeRef> Volumes { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, string> Secrets { get; } = new(StringComparer.Ordinal);
    internal HashSet<string> UsedNetworks { get; } = new(StringComparer.Ordinal);
    internal HashSet<string> HealthcheckedServices { get; } = services
        .Where(s => Map(s.Value)?.GetValueOrDefault("healthcheck") is not null)
        .Select(s => s.Key)
        .ToHashSet(StringComparer.Ordinal);
}
