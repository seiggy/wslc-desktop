using System.Text.Json;
using WslcDesktop.Compose.Planning;
using WslcDesktop.Compose.Secrets.Injection;

namespace WslcDesktop.Compose.Execution;

/// <summary>Executes compose plans against wslc: up, down and start/stop, with health and completion waits.</summary>
public sealed class ComposeRunner(WslcExec exec, Action<string> log, WslcExecEnv? execEnv = null, Func<string, string?>? secrets = null)
{
    public TimeSpan WaitTimeout { get; init; } = TimeSpan.FromMinutes(3);

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    private async Task<string> ExecRunAsync(IReadOnlyList<string> args)
    {
        if (!SecretTokens.ContainsAny(args))
        {
            return await exec(args);
        }

        if (secrets is null || execEnv is null)
        {
            throw new InvalidOperationException("This stack uses secrets, but no secret store is available.");
        }

        using var prepared = SecretInjection.Prepare(args, secrets);
        return await execEnv(prepared.Args, prepared.Environment);
    }

    public async Task UpAsync(ComposePlan plan, CancellationToken ct = default)
    {
        if (!plan.CanRun)
        {
            throw new InvalidOperationException("The plan has errors and can't be run.");
        }

        var networks = await ListNamesAsync("network", "list");
        var volumes = await ListNamesAsync("volume", "list");
        var existing = (await ListAsync()).ToDictionary(c => c.Name, StringComparer.Ordinal);
        var containerByService = plan.Actions
            .Where(a => a.Kind == ActionKind.RunService)
            .ToDictionary(a => a.Service!, a => a.Target, StringComparer.Ordinal);

        foreach (var action in plan.Actions)
        {
            ct.ThrowIfCancellationRequested();
            switch (action.Kind)
            {
                case ActionKind.CreateNetwork:
                    await CreateIfMissingAsync(action, networks, "network");
                    break;
                case ActionKind.CreateVolume:
                    await CreateIfMissingAsync(action, volumes, "volume");
                    break;
                case ActionKind.Build:
                    log($"Building {action.Target}…");
                    await exec(action.Args);
                    break;
                case ActionKind.RunService:
                    await RunServiceAsync(plan, action, existing, containerByService, ct);
                    break;
            }
        }

        log($"Stack '{plan.Project}' is up.");
    }

    private async Task CreateIfMissingAsync(ComposeAction action, HashSet<string> known, string noun)
    {
        if (known.Contains(action.Target))
        {
            log($"{noun} {action.Target} already exists.");
            return;
        }

        log($"Creating {noun} {action.Target}…");
        await exec(action.Args);
        known.Add(action.Target);
    }

    private async Task RunServiceAsync(
        ComposePlan plan, ComposeAction action, Dictionary<string, StackContainerInfo> existing,
        Dictionary<string, string> containerByService, CancellationToken ct)
    {
        foreach (var wait in action.Waits ?? [])
        {
            await WaitForAsync(containerByService[wait.Service], wait, ct);
        }

        if (existing.TryGetValue(action.Target, out var current))
        {
            if (current.Project != plan.Project)
            {
                throw new InvalidOperationException(
                    $"A container named '{action.Target}' already exists and doesn't belong to stack '{plan.Project}'.");
            }

            if (ContainerState.IsRunning(current.State))
            {
                log($"{action.Service} is already running.");
                return;
            }

            log($"Starting existing {action.Service}…");
            await exec(["start", action.Target]);
            return;
        }

        log($"Starting {action.Service}…");
        await ExecRunAsync(action.Args);
        foreach (var extra in action.After ?? [])
        {
            await exec(extra);
        }
    }

    private async Task WaitForAsync(string container, ServiceWait wait, CancellationToken ct)
    {
        if (wait.Condition == "service_started")
        {
            return;
        }

        log($"Waiting for {wait.Service} to be {(wait.Condition == "service_healthy" ? "healthy" : "completed")}…");
        var deadline = DateTime.UtcNow + WaitTimeout;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var (status, health, exit) = await InspectStateAsync(container);
            if (wait.Condition == "service_healthy")
            {
                if (health == "healthy")
                {
                    return;
                }

                if (health == "unhealthy" || status is "exited" or "dead")
                {
                    throw new InvalidOperationException($"Service '{wait.Service}' is unhealthy; dependent services were not started.");
                }
            }
            else if (status is "exited" or "dead")
            {
                if (exit == 0)
                {
                    return;
                }

                throw new InvalidOperationException($"Service '{wait.Service}' exited with code {exit}; dependent services were not started.");
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting for service '{wait.Service}'.");
            }

            await Task.Delay(PollInterval, ct);
        }
    }

    private async Task<(string Status, string Health, int ExitCode)> InspectStateAsync(string container)
    {
        using var doc = JsonDocument.Parse(await exec(["inspect", container]));
        var root = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement[0] : doc.RootElement;
        var state = root.GetProperty("State");
        var health = state.TryGetProperty("Health", out var h) && h.ValueKind == JsonValueKind.Object && h.TryGetProperty("Status", out var hs)
            ? hs.GetString() ?? string.Empty
            : string.Empty;
        return (
            state.TryGetProperty("Status", out var s) ? s.GetString() ?? string.Empty : string.Empty,
            health,
            state.TryGetProperty("ExitCode", out var e) && e.TryGetInt32(out var code) ? code : 0);
    }

    public async Task StartAsync(string project)
    {
        var names = (await ListAsync()).Where(c => c.Project == project && !ContainerState.IsRunning(c.State)).Select(c => c.Name).ToList();
        if (names.Count > 0)
        {
            log($"Starting {names.Count} container(s)…");
            await exec(["start", .. names]);
        }
    }

    public async Task StopAsync(string project)
    {
        var names = (await ListAsync()).Where(c => c.Project == project && ContainerState.IsRunning(c.State)).Select(c => c.Name).ToList();
        if (names.Count > 0)
        {
            log($"Stopping {names.Count} container(s)…");
            await exec(["stop", .. names]);
        }
    }

    /// <summary>Stops and removes the stack's containers and its project-prefixed networks (and, optionally, volumes).</summary>
    public async Task DownAsync(string project, bool removeVolumes)
    {
        var containers = (await ListAsync()).Where(c => c.Project == project).ToList();
        var networks = new HashSet<string>(StringComparer.Ordinal);
        var volumes = new HashSet<string>(StringComparer.Ordinal);
        var prefix = project + "_";
        foreach (var c in containers)
        {
            try
            {
                using var doc = JsonDocument.Parse(await exec(["inspect", c.Name]));
                var root = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement[0] : doc.RootElement;
                if (root.TryGetProperty("NetworkSettings", out var ns) && ns.TryGetProperty("Networks", out var nets) && nets.ValueKind == JsonValueKind.Object)
                {
                    networks.UnionWith(nets.EnumerateObject().Select(n => n.Name).Where(n => n.StartsWith(prefix, StringComparison.Ordinal)));
                }

                if (root.TryGetProperty("Mounts", out var mounts) && mounts.ValueKind == JsonValueKind.Array)
                {
                    volumes.UnionWith(mounts.EnumerateArray()
                        .Where(m => m.TryGetProperty("Type", out var t) && t.GetString() == "volume" && m.TryGetProperty("Name", out _))
                        .Select(m => m.GetProperty("Name").GetString() ?? string.Empty)
                        .Where(n => n.StartsWith(prefix, StringComparison.Ordinal)));
                }
            }
            catch (Exception e) when (e is InvalidOperationException or JsonException)
            {
                log($"Couldn't inspect {c.Name}: {e.Message}");
            }
        }

        var names = containers.Select(c => c.Name).ToList();
        if (names.Count > 0)
        {
            log($"Stopping {names.Count} container(s)…");
            await exec(["stop", .. names]);
            log("Removing containers…");
            await exec(["rm", "--force", .. names]);
        }

        foreach (var n in networks)
        {
            await TryAsync($"Removing network {n}…", ["network", "remove", n]);
        }

        if (removeVolumes)
        {
            foreach (var v in volumes)
            {
                await TryAsync($"Removing volume {v}…", ["volume", "remove", v]);
            }
        }

        log($"Stack '{project}' is down.");
    }

    private async Task TryAsync(string message, string[] args)
    {
        log(message);
        try
        {
            await exec(args);
        }
        catch (InvalidOperationException e)
        {
            log($"  skipped: {e.Message}");
        }
    }

    private sealed record StackContainerInfo(string Name, string State, string Project, string Service);

    private async Task<List<StackContainerInfo>> ListAsync()
    {
        var result = new List<StackContainerInfo>();
        foreach (var line in (await exec(["list", "--all", "--format", "json", "--no-trunc"])).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith('{'))
            {
                continue;
            }

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            ComposeLabels.TryParse(Str(root, "Labels"), out var project, out var service);
            result.Add(new StackContainerInfo(Str(root, "Names"), Str(root, "State"), project, service));
        }

        return result;
    }

    private async Task<HashSet<string>> ListNamesAsync(string noun, string verb)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in (await exec([noun, verb, "--format", "json"])).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith('{'))
            {
                using var doc = JsonDocument.Parse(line);
                var name = Str(doc.RootElement, "Name");
                if (name.Length > 0)
                {
                    set.Add(name);
                }
            }
        }

        return set;
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
}
