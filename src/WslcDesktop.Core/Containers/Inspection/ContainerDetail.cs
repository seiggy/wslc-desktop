using System.Text.Json;
using WslcDesktop.Common;

namespace WslcDesktop.Containers.Inspection;

public sealed record ContainerDetail(
    string Name,
    string Id,
    string Image,
    string ImageId,
    string Status,
    string Created,
    string Started,
    string Finished,
    string ExitCode,
    string Health,
    string MemoryLimit,
    string CpuLimit,
    string NetworkMode,
    IReadOnlyList<string> Ports,
    IReadOnlyList<NetworkInfo> Networks,
    IReadOnlyList<string> Mounts,
    IReadOnlyList<string> Entrypoint,
    IReadOnlyList<string> Cmd,
    string WorkingDir,
    string User,
    IReadOnlyList<string> Env,
    IReadOnlyList<string> Labels,
    string RawJson)
{
    /// <summary>Parses <c>wslc inspect</c> output; any missing field becomes empty rather than failing.</summary>
    public static ContainerDetail Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0
            ? doc.RootElement[0]
            : doc.RootElement;

        var config = Obj(root, "Config");
        var host = Obj(root, "HostConfig");
        var state = Obj(root, "State");

        var memory = Num(host, "Memory");
        var nanoCpus = Num(host, "NanoCpus");
        var health = Obj(state, "Health");

        var ports = new List<string>();
        if (Obj(root, "Ports") is { ValueKind: JsonValueKind.Object } portMap)
        {
            foreach (var p in portMap.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.Array && p.Value.GetArrayLength() > 0)
                {
                    ports.AddRange(p.Value.EnumerateArray().Select(b =>
                        $"{Str(b, "HostIp")}:{Str(b, "HostPort")} → {p.Name}"));
                }
                else
                {
                    ports.Add($"{p.Name} (not published)");
                }
            }
        }

        var networks = new List<NetworkInfo>();
        if (Obj(Obj(root, "NetworkSettings"), "Networks") is { ValueKind: JsonValueKind.Object } nets)
        {
            networks.AddRange(nets.EnumerateObject().Select(n => new NetworkInfo(
                n.Name, Str(n.Value, "IPAddress"), Str(n.Value, "Gateway"), Str(n.Value, "MacAddress"))));
        }

        var mounts = new List<string>();
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Mounts", out var m) && m.ValueKind == JsonValueKind.Array)
        {
            mounts.AddRange(m.EnumerateArray().Select(DescribeMount));
        }

        var exit = state.ValueKind == JsonValueKind.Object && state.TryGetProperty("ExitCode", out var ec) && ec.ValueKind == JsonValueKind.Number
            ? ec.GetInt64().ToString()
            : string.Empty;

        return new ContainerDetail(
            Str(root, "Name").TrimStart('/'),
            Str(root, "Id"),
            Str(config, "Image"),
            Str(root, "Image"),
            Str(state, "Status"),
            Str(root, "Created"),
            Str(state, "StartedAt"),
            Str(state, "FinishedAt"),
            exit,
            health.ValueKind == JsonValueKind.Object ? Str(health, "Status") : string.Empty,
            memory > 0 ? SizeFormat.Format((ulong)memory) : string.Empty,
            nanoCpus > 0 ? $"{nanoCpus / 1e9:0.##} CPUs" : string.Empty,
            Str(host, "NetworkMode"),
            ports,
            networks,
            mounts,
            Strings(config, "Entrypoint"),
            Strings(config, "Cmd"),
            Str(config, "WorkingDir"),
            Str(config, "User"),
            Strings(config, "Env"),
            Obj(root, "Labels") is { ValueKind: JsonValueKind.Object } labels
                ? labels.EnumerateObject().Select(l => $"{l.Name}={l.Value}").ToList()
                : [],
            json);
    }

    private static string DescribeMount(JsonElement m)
    {
        if (m.ValueKind != JsonValueKind.Object)
        {
            return m.ToString();
        }

        var source = Str(m, "Source");
        var target = Str(m, "Destination");
        if (target.Length == 0)
        {
            target = Str(m, "Target");
        }

        var kind = Str(m, "Type");
        var mode = m.TryGetProperty("RW", out var rw) && rw.ValueKind == JsonValueKind.False ? "read-only" : string.Empty;
        var text = source.Length > 0 || target.Length > 0 ? $"{source} → {target}" : m.ToString();
        return string.Join(" · ", new[] { text, kind, mode }.Where(s => s.Length > 0));
    }

    private static JsonElement Obj(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty
            : string.Empty;

    private static long Num(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) ? n : 0;

    private static List<string> Strings(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToList()
            : [];
}
