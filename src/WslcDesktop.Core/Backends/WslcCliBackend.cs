using WslcDesktop.Localization;
using System.Globalization;
using System.Text.Json;
using WslcDesktop.Cli;
using WslcDesktop.Compose.Secrets.Injection;
using WslcDesktop.Containers;
using WslcDesktop.Containers.Creation;
using WslcDesktop.Images;
using WslcDesktop.Resources;

namespace WslcDesktop.Backends;

/// <summary>Drives one WSLc session through wslc.exe. Used for other sessions and for an app session attached from an earlier run.</summary>
public sealed class WslcCliBackend(IWslcProcess process, string session, Func<string, string?> lookupSecret) : ISessionBackend
{
    private readonly Func<string, string?> _lookupSecret = lookupSecret ?? throw new ArgumentNullException(nameof(lookupSecret));
    private static readonly string[] BuiltInNetworks = ["bridge", "host", "none"];

    private static readonly TimeSpan RegistryTimeout = TimeSpan.FromSeconds(60);

    public string Session { get; } = session;

    private Task<string> RunAsync(params string[] args) =>
        process.RunAsync(new WslcInvocation(["--session", Session, .. args]));

    // Images

    public Task DeleteImageAsync(string target) => RunAsync("rmi", target);

    public Task PullImageAsync(string uri, IProgress<PullProgress>? progress) => RunAsync("pull", uri);

    public async Task<IReadOnlyList<ImageSummary>> ListImagesAsync() =>
        WslcCli.ParseImages(await RunAsync("images", "--no-trunc", "--format", "json"));

    // Containers

    public Task StartContainerAsync(string name) => RunAsync("start", name);

    public Task StreamLogsAsync(string container, LogOptions options, Action<string> onLine, CancellationToken ct)
    {
        var args = new List<string> { "--session", Session, "logs", "--tail", options.Tail.ToString(CultureInfo.InvariantCulture) };
        if (options.Follow)
        {
            args.Add("--follow");
        }

        if (options.Timestamps)
        {
            args.Add("--timestamps");
        }

        args.Add(container);
        return process.StreamAsync(new WslcInvocation(args), onLine, ct);
    }

    public IShellProcess StartShell(string container, string shell, Action<string> onOutput, Action<int> onExit) =>
        process.StartInteractive(new WslcInvocation(["--session", Session, "exec", "--interactive", container, shell]), onOutput, onExit);

    public Task StopContainerAsync(string name) => RunAsync("stop", name);

    public Task DeleteContainerAsync(string name) => RunAsync("rm", "--force", name);

    public Task<string> InspectContainerAsync(string name) => RunAsync("inspect", name);

    public async Task<IReadOnlyList<ContainerRow>> ListContainersAsync() =>
        WslcCli.ParseContainers(await RunAsync("list", "--all", "--no-trunc", "--format", "json"));

    public async Task CreateContainerAsync(NewContainerSpec spec)
    {
        var args = new List<string> { "create", "--name", spec.Name };
        foreach (var p in spec.Ports)
        {
            args.AddRange(["--publish", p]);
        }

        foreach (var m in spec.Mounts)
        {
            args.AddRange(["--volume", m]);
        }

        foreach (var e in spec.Env)
        {
            args.AddRange(["--env", e]);
        }

        if (spec.Network.Length > 0)
        {
            args.AddRange(["--network", spec.Network]);
        }

        args.Add(spec.Image);
        args.AddRange(spec.Command);
        if (SecretTokens.ContainsAny(args))
        {
            using var prepared = SecretInjection.Prepare(args, _lookupSecret);
            await process.RunAsync(new WslcInvocation(["--session", Session, .. prepared.Args], prepared.Environment));
            return;
        }

        await RunAsync([.. args]);
    }

    /// <summary>Creates a container whose environment values are passed as inherited variables, so they never reach a command line.</summary>
    public Task CreateContainerAsync(string name, string image, string[] commandLine, IReadOnlyDictionary<string, string> environment)
    {
        var args = new List<string> { "--session", Session, "create", "--name", name };
        foreach (var key in environment.Keys)
        {
            args.AddRange(["--env", key]);
        }

        args.Add(image);
        args.AddRange(commandLine);
        return process.RunAsync(new WslcInvocation(args, environment));
    }

    // Volumes and networks

    public Task CreateVolumeAsync(string name, string driver, long? sizeBytes) =>
        process.RunAsync(new WslcInvocation(VolumeSize.CreateArguments(Session, name, driver, sizeBytes)));

    public Task CreateNetworkAsync(string name, string driver, string subnet, string gateway, bool isInternal)
    {
        var args = new List<string> { "network", "create", "--driver", driver };
        if (subnet.Length > 0)
        {
            args.AddRange(["--subnet", subnet]);
        }

        if (gateway.Length > 0)
        {
            args.AddRange(["--gateway", gateway]);
        }

        if (isInternal)
        {
            args.Add("--internal");
        }

        args.Add(name);
        return RunAsync([.. args]);
    }

    public Task RemoveResourceAsync(ResourceKind kind, string name) => RunAsync(Noun(kind), "remove", name);

    public Task PruneResourcesAsync(ResourceKind kind) =>
        kind == ResourceKind.Volume
            ? RunAsync("volume", "prune", "--all", "--force")
            : RunAsync("network", "prune", "--force");

    public Task ConnectNetworkAsync(string network, string container, string alias)
    {
        var args = new List<string> { "network", "connect" };
        if (alias.Length > 0)
        {
            args.AddRange(["--network-alias", alias]);
        }

        args.AddRange([network, container]);
        return RunAsync([.. args]);
    }

    public Task DisconnectNetworkAsync(string network, string container) =>
        RunAsync("network", "disconnect", network, container);

    public Task<string> InspectResourceAsync(ResourceKind kind, string name) =>
        RunAsync(Noun(kind), "inspect", "--format", "json", name);

    public async Task<IReadOnlyList<ResourceRow>> ListResourcesAsync(ResourceKind kind)
    {
        var listing = RunAsync(Noun(kind), "list", "--format", "json");
        var usage = await GetUsageAsync();
        var rows = new List<ResourceRow>();
        foreach (var o in JsonLines(await listing))
        {
            var name = Str(o, "Name");
            rows.Add(kind == ResourceKind.Volume
                ? new ResourceRow(name, Str(o, "Driver"), Str(o, "Mountpoint"), Str(o, "Scope"), true,
                    usage.Volumes.GetValueOrDefault(name) ?? [])
                : new ResourceRow(name, Str(o, "Driver"), Str(o, "Scope"),
                    Str(o, "Internal") == "true" ? UiStrings.Get("Internal") : UiStrings.Get("External"),
                    !BuiltInNetworks.Contains(name),
                    usage.Networks.GetValueOrDefault(name) ?? []));
        }

        return rows;
    }

    private async Task<(Dictionary<string, List<string>> Volumes, Dictionary<string, List<string>> Networks)> GetUsageAsync()
    {
        var volumes = new Dictionary<string, List<string>>();
        var networks = new Dictionary<string, List<string>>();
        var containers = await ListContainersAsync();
        var inspected = await Task.WhenAll(containers.Select(async c =>
        {
            try
            {
                return (c.Name, Json: await InspectContainerAsync(c.Name));
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
                        AddUsage(volumes, Str(m, "Name"), name);
                    }
                }

                if (root.TryGetProperty("NetworkSettings", out var ns) && ns.ValueKind == JsonValueKind.Object
                    && ns.TryGetProperty("Networks", out var nets) && nets.ValueKind == JsonValueKind.Object)
                {
                    foreach (var n in nets.EnumerateObject())
                    {
                        AddUsage(networks, n.Name, name);
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        return (volumes, networks);
    }

    // Registry

    public Task RegistryLoginAsync(string server, string username, string password)
    {
        var args = new List<string> { "--session", Session, "registry", "login", "--username", username, "--password-stdin" };
        if (server.Length > 0)
        {
            args.Add(server);
        }

        return process.RunAsync(new WslcInvocation(args, StandardInput: password, Timeout: RegistryTimeout));
    }

    public Task RegistryLogoutAsync(string server)
    {
        var args = new List<string> { "--session", Session, "registry", "logout" };
        if (server.Length > 0)
        {
            args.Add(server);
        }

        return process.RunAsync(new WslcInvocation(args, Timeout: RegistryTimeout));
    }

    private static string Noun(ResourceKind kind) => kind == ResourceKind.Volume ? "volume" : "network";

    private static void AddUsage(Dictionary<string, List<string>> map, string key, string container)
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

    private static IEnumerable<JsonElement> JsonLines(string output)
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
