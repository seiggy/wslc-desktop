using System.ComponentModel;
using System.Text.Json;
using WslcDesktop.Compose;

namespace WslcDesktop.Services;

public enum ResourceKind
{
    Volume,
    Network,
}

public sealed record ResourceRow(string Name, string Col1, string Col2, string Col3, bool CanRemove, IReadOnlyList<string> UsedBy);

public sealed record NewContainerSpec(
    string Name,
    string Image,
    string[] Command,
    IReadOnlyList<string> Ports,
    IReadOnlyList<string> Mounts,
    IReadOnlyList<string> Env,
    string Network);

/// <summary>A volume or network row that is updated in place when a poll reports a change.</summary>
public sealed class ResourceItem(ResourceRow row) : INotifyPropertyChanged
{
    private ResourceRow _r = row;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name => _r.Name;

    public string Col1 => _r.Col1;

    public string Col2 => _r.Col2;

    public string Col3 => _r.Col3;

    public bool CanRemove => _r.CanRemove;

    public string UsedByText => _r.UsedBy.Count switch
    {
        0 => "Unused",
        1 => _r.UsedBy[0],
        _ => string.Join(", ", _r.UsedBy),
    };

    public void Update(ResourceRow next)
    {
        if (next.Name == _r.Name && next.Col1 == _r.Col1 && next.Col2 == _r.Col2 && next.Col3 == _r.Col3
            && next.CanRemove == _r.CanRemove && next.UsedBy.SequenceEqual(_r.UsedBy))
        {
            return;
        }

        _r = next;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}

/// <summary>Volume and network management through wslc.exe.</summary>
public static class WslcResources
{
    private static readonly string[] BuiltInNetworks = ["bridge", "host", "none"];

    public static IReadOnlyList<string> BuiltIns => BuiltInNetworks;

    public static async Task<(Dictionary<string, List<string>> Volumes, Dictionary<string, List<string>> Networks)> GetUsageAsync(string session)
    {
        var volumes = new Dictionary<string, List<string>>();
        var networks = new Dictionary<string, List<string>>();
        var containers = await WslcCli.ListContainersAsync(session);
        var inspected = await Task.WhenAll(containers.Select(async c =>
        {
            try
            {
                return (c.Name, Json: await WslcCli.RunAsync("--session", session, "inspect", c.Name));
            }
            catch
            {
                return (c.Name, Json: string.Empty);
            }
        }));

        foreach (var (name, json) in inspected)
        {
            if (json.Length == 0)
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0
                    ? doc.RootElement[0]
                    : doc.RootElement;
                if (root.TryGetProperty("Mounts", out var mounts) && mounts.ValueKind == JsonValueKind.Array)
                {
                    foreach (var m in mounts.EnumerateArray().Where(m => Str(m, "Type") == "volume"))
                    {
                        Add(volumes, Str(m, "Name"), name);
                    }
                }

                if (root.TryGetProperty("NetworkSettings", out var ns) && ns.ValueKind == JsonValueKind.Object
                    && ns.TryGetProperty("Networks", out var nets) && nets.ValueKind == JsonValueKind.Object)
                {
                    foreach (var n in nets.EnumerateObject())
                    {
                        Add(networks, n.Name, name);
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        return (volumes, networks);
    }

    public static async Task<IReadOnlyList<ResourceRow>> ListVolumesAsync(string session)
    {
        var listing = WslcCli.RunAsync("--session", session, "volume", "list", "--format", "json");
        var usage = await GetUsageAsync(session);
        var rows = new List<ResourceRow>();
        foreach (var o in Lines(await listing))
        {
            var name = Str(o, "Name");
            rows.Add(new ResourceRow(name, Str(o, "Driver"), Str(o, "Mountpoint"), Str(o, "Scope"), true,
                usage.Volumes.GetValueOrDefault(name) ?? []));
        }

        return rows;
    }

    public static async Task<IReadOnlyList<ResourceRow>> ListNetworksAsync(string session)
    {
        var listing = WslcCli.RunAsync("--session", session, "network", "list", "--format", "json");
        var usage = await GetUsageAsync(session);
        var rows = new List<ResourceRow>();
        foreach (var o in Lines(await listing))
        {
            var name = Str(o, "Name");
            rows.Add(new ResourceRow(name, Str(o, "Driver"), Str(o, "Scope"),
                Str(o, "Internal") == "true" ? "Internal" : "External",
                !BuiltInNetworks.Contains(name),
                usage.Networks.GetValueOrDefault(name) ?? []));
        }

        return rows;
    }

    public static async Task<IReadOnlyList<string>> ListNetworkNamesAsync(string session) =>
        (await ListNetworksAsync(session)).Select(r => r.Name).ToList();

    public static async Task<string> InspectAsync(string session, ResourceKind kind, string name) =>
        SecretService.Default.Redact(await WslcCli.RunAsync("--session", session, Noun(kind), "inspect", "--format", "json", name));

    public static Task CreateVolumeAsync(string session, string name, string driver, long? sizeBytes = null) =>
        WslcCli.RunAsync(VolumeSize.CreateArguments(session, name, driver, sizeBytes));

    public static Task CreateNetworkAsync(string session, string name, string driver, string subnet, string gateway, bool isInternal)
    {
        var args = new List<string> { "--session", session, "network", "create", "--driver", driver };
        if (subnet.Length > 0)
        {
            args.AddRange(new[] { "--subnet", subnet });
        }

        if (gateway.Length > 0)
        {
            args.AddRange(new[] { "--gateway", gateway });
        }

        if (isInternal)
        {
            args.Add("--internal");
        }

        args.Add(name);
        return WslcCli.RunAsync(args.ToArray());
    }

    public static Task RemoveAsync(string session, ResourceKind kind, string name) =>
        WslcCli.RunAsync("--session", session, Noun(kind), "remove", name);

    public static Task PruneAsync(string session, ResourceKind kind) =>
        kind == ResourceKind.Volume
            ? WslcCli.RunAsync("--session", session, "volume", "prune", "--all", "--force")
            : WslcCli.RunAsync("--session", session, "network", "prune", "--force");

    public static Task ConnectAsync(string session, string network, string container, string alias)
    {
        var args = new List<string> { "--session", session, "network", "connect" };
        if (alias.Length > 0)
        {
            args.AddRange(new[] { "--network-alias", alias });
        }

        args.AddRange(new[] { network, container });
        return WslcCli.RunAsync(args.ToArray());
    }

    public static Task DisconnectAsync(string session, string network, string container) =>
        WslcCli.RunAsync("--session", session, "network", "disconnect", network, container);

    public static async Task CreateContainerAsync(string session, NewContainerSpec spec)
    {
        var args = new List<string> { "--session", session, "create", "--name", spec.Name };
        foreach (var p in spec.Ports)
        {
            args.AddRange(new[] { "--publish", p });
        }

        foreach (var m in spec.Mounts)
        {
            args.AddRange(new[] { "--volume", m });
        }

        foreach (var e in spec.Env)
        {
            args.AddRange(new[] { "--env", e });
        }

        if (spec.Network.Length > 0)
        {
            args.AddRange(new[] { "--network", spec.Network });
        }

        args.Add(spec.Image);
        args.AddRange(spec.Command);
        if (SecretTokens.ContainsAny(args))
        {
            using var prepared = SecretInjection.Prepare(args, SecretService.Default.Get);
            await WslcCli.RunWithEnvAsync(prepared.Environment, prepared.Args);
            return;
        }

        await WslcCli.RunAsync(args.ToArray());
    }

    private static string Noun(ResourceKind kind) => kind == ResourceKind.Volume ? "volume" : "network";

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

    private static IEnumerable<JsonElement> Lines(string output)
    {
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith('{'))
            {
                using var doc = JsonDocument.Parse(line);
                yield return doc.RootElement.Clone();
            }
        }
    }

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty
            : string.Empty;
}
