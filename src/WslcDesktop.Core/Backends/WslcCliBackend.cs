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

    private readonly ContainerListMemo _containers = new();

    public async Task<IReadOnlyList<ContainerRow>> ListContainersAsync() =>
        _containers.Parse(Session, await RunAsync("list", "--all", "--no-trunc", "--format", "json"));

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
        return Rows(kind, await listing, usage);
    }

    public async Task<IReadOnlyList<ResourceRow>> ListResourceRowsAsync(ResourceKind kind) =>
        Rows(kind, await RunAsync(Noun(kind), "list", "--format", "json"), null);

    public async Task<IReadOnlyDictionary<string, string>> InspectContainersAsync(IReadOnlyList<string> names) =>
        names.Count == 0
            ? new Dictionary<string, string>()
            : (await InspectAllAsync(names)).GroupBy(i => i.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Root.GetRawText(), StringComparer.Ordinal);

    private static List<ResourceRow> Rows(ResourceKind kind, string listing, ResourceUsage? usage)
    {
        var rows = new List<ResourceRow>();
        foreach (var o in JsonLines(listing))
        {
            var name = Str(o, "Name");
            var usedBy = usage?.For(kind, name) ?? [];
            rows.Add(kind == ResourceKind.Volume
                ? new ResourceRow(name, Str(o, "Driver"), Str(o, "Mountpoint"), Str(o, "Scope"), true, usedBy)
                : new ResourceRow(name, Str(o, "Driver"), Str(o, "Scope"),
                    Str(o, "Internal") == "true" ? UiStrings.Get("Internal") : UiStrings.Get("External"),
                    !ResourceUsage.BuiltInNetworks.Contains(name),
                    usedBy));
        }

        return rows;
    }

    /// <summary>Just the network names: no container inspection, unlike <see cref="ListResourcesAsync"/>.</summary>
    public async Task<IReadOnlyList<string>> ListNetworkNamesAsync() =>
        JsonLines(await RunAsync("network", "list", "--format", "json")).Select(o => Str(o, "Name")).Where(n => n.Length > 0).ToList();

    private async Task<ResourceUsage> GetUsageAsync()
    {
        var containers = await ListContainersAsync();
        return containers.Count == 0
            ? new ResourceUsage()
            : ResourceUsage.From(await InspectAllAsync(containers.Select(c => c.Name).ToList()));
    }

    /// <summary>Inspects every container in one wslc call, falling back to one call each if the batch fails.</summary>
    private async Task<List<(string Name, JsonElement Root)>> InspectAllAsync(IReadOnlyList<string> names)
    {
        string batch;
        try
        {
            batch = await RunAsync(["inspect", "--type", "container", "--format", "json", .. names]);
        }
        catch (InvalidOperationException)
        {
            // One container removed between the listing and the inspect fails the whole batch.
            var single = await Task.WhenAll(names.Select(async n =>
            {
                try
                {
                    return (Name: n, Objects: ResourceUsage.Objects(await InspectContainerAsync(n)).Take(1).ToList());
                }
                catch
                {
                    return (Name: n, Objects: new List<JsonElement>());
                }
            }));
            return single.SelectMany(s => s.Objects.Select(o => (s.Name, o))).ToList();
        }

        var objects = ResourceUsage.Objects(batch);
        var result = new List<(string, JsonElement)>(objects.Count);
        for (var i = 0; i < objects.Count; i++)
        {
            var reported = Str(objects[i], "Name").TrimStart('/');
            var name = reported.Length > 0 ? reported : i < names.Count ? names[i] : string.Empty;
            if (name.Length > 0)
            {
                result.Add((name, objects[i]));
            }
        }

        return result;
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
