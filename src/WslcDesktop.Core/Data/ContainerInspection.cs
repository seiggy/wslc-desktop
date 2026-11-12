using WslcDesktop.Resources;

namespace WslcDesktop.Data;

/// <summary>
/// Every container of a session inspected in one read: the raw JSON per container (for detail pages) and the
/// volume/network usage derived from it (for the Volumes and Networks lists).
/// </summary>
public sealed class ContainerInspection
{
    public static readonly ContainerInspection Empty = new(new Dictionary<string, string>(StringComparer.Ordinal));

    private ContainerInspection(IReadOnlyDictionary<string, string> json)
    {
        Json = json;
        Usage = ResourceUsage.From(json.SelectMany(kv => ResourceUsage.Objects(kv.Value).Take(1).Select(o => (kv.Key, o))));
    }

    /// <summary>Unredacted inspect JSON by container name.</summary>
    public IReadOnlyDictionary<string, string> Json { get; }

    public ResourceUsage Usage { get; }

    public static ContainerInspection From(IReadOnlyDictionary<string, string> json) =>
        json.Count == 0 ? Empty : new ContainerInspection(new Dictionary<string, string>(json, StringComparer.Ordinal));

    public static bool Same(ContainerInspection a, ContainerInspection b) =>
        a.Json.Count == b.Json.Count
        && a.Json.All(kv => b.Json.TryGetValue(kv.Key, out var other) && string.Equals(kv.Value, other, StringComparison.Ordinal));
}
