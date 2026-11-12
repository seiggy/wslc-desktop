using System.Text.Json;

namespace WslcDesktop.Resources;

/// <summary>Which containers use each volume and network, read from container inspect JSON.</summary>
public sealed class ResourceUsage
{
    public static readonly string[] BuiltInNetworks = ["bridge", "host", "none"];

    public Dictionary<string, List<string>> Volumes { get; } = new();

    public Dictionary<string, List<string>> Networks { get; } = new();

    public IReadOnlyList<string> For(ResourceKind kind, string name) =>
        (kind == ResourceKind.Volume ? Volumes : Networks).GetValueOrDefault(name) ?? [];

    public static ResourceUsage From(IEnumerable<(string Container, JsonElement Inspect)> containers)
    {
        var usage = new ResourceUsage();
        foreach (var (name, root) in containers)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (root.TryGetProperty("Mounts", out var mounts) && mounts.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in mounts.EnumerateArray().Where(m => Str(m, "Type") == "volume"))
                {
                    Add(usage.Volumes, Str(m, "Name"), name);
                }
            }

            if (root.TryGetProperty("NetworkSettings", out var ns) && ns.ValueKind == JsonValueKind.Object
                && ns.TryGetProperty("Networks", out var nets) && nets.ValueKind == JsonValueKind.Object)
            {
                foreach (var n in nets.EnumerateObject())
                {
                    Add(usage.Networks, n.Name, name);
                }
            }
        }

        return usage;
    }

    /// <summary>The inspected objects in <paramref name="json"/>: an array of them, or a single object.</summary>
    public static List<JsonElement> Objects(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return root.ValueKind switch
            {
                JsonValueKind.Array => root.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).Select(e => e.Clone()).ToList(),
                JsonValueKind.Object => [root.Clone()],
                _ => [],
            };
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void Add(Dictionary<string, List<string>> map, string key, string container)
    {
        if (key.Length == 0)
        {
            return;
        }

        if (!map.TryGetValue(key, out var list))
        {
            map[key] = list = [];
        }

        if (!list.Contains(container))
        {
            list.Add(container);
        }
    }

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty
            : string.Empty;
}
