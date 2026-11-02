using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WslcDesktop.Compose;

/// <summary>Translates a compose file into an ordered list of wslc invocations plus warnings for anything WSLc can't do.</summary>
public static partial class ComposePlanner
{
    private static readonly HashSet<string> SupportedServiceKeys =
    [
        "image", "build", "command", "entrypoint", "environment", "env_file", "working_dir", "user", "ports", "expose",
        "volumes", "networks", "network_mode", "healthcheck", "mem_limit", "cpus", "depends_on", "container_name",
        "hostname", "domainname", "labels", "stop_signal", "stop_grace_period", "tty", "stdin_open", "shm_size",
        "tmpfs", "dns", "dns_search", "pull_policy",
    ];

    private static readonly Dictionary<string, string> UnsupportedServiceKeys = new(StringComparer.Ordinal)
    {
        ["restart"] = "WSLc has no restart policy, so this container won't restart automatically",
        ["deploy"] = "deploy (replicas, resources, placement, restart_policy) isn't supported; a single container is started with no resource limits from it",
        ["profiles"] = "profiles aren't supported; this service is always started",
        ["secrets"] = "secrets aren't supported and won't be mounted",
        ["configs"] = "configs aren't supported and won't be mounted",
        ["extends"] = "extends isn't supported; the service is not merged with the referenced one",
        ["links"] = "links aren't supported; use networks and service names instead",
        ["platform"] = "platform selection isn't supported",
        ["scale"] = "scale isn't supported; a single container is started",
    };

    private static readonly HashSet<string> SupportedBuildKeys = ["context", "dockerfile", "args", "target"];

    public static ComposePlan Plan(string yaml, ComposeOptions options)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var project = string.Empty;
        var actions = new List<ComposeAction>();
        try
        {
            var root = YamlTree.ParseDocument(yaml);
            root = (Dictionary<string, object?>)Interpolator.ApplyTree(root, options.Env)!;
            project = ProjectName(root, options);
            Build(root, project, options, actions, warnings, errors);
        }
        catch (ComposeException e)
        {
            errors.Add(e.Message);
        }

        return new ComposePlan(project, actions, warnings, errors);
    }

    public static string NormalizeProjectName(string name)
    {
        var lower = InvalidNameChars().Replace(name.Trim().ToLowerInvariant(), "-");
        return lower.TrimStart('-', '_');
    }

    private static string ProjectName(Dictionary<string, object?> root, ComposeOptions options)
    {
        var raw = !string.IsNullOrWhiteSpace(options.ProjectName)
            ? options.ProjectName
            : root.GetValueOrDefault("name") as string
              ?? Path.GetFileName(options.Directory.TrimEnd('\\', '/'));
        var name = NormalizeProjectName(raw ?? string.Empty);
        return name.Length > 0 ? name : throw new ComposeException("Couldn't derive a project name; set a name: or choose one.");
    }

    private sealed record NetworkRef(string Key, string Actual, bool External);

    private sealed record VolumeRef(string Actual, bool External);

    private static void Build(
        Dictionary<string, object?> root, string project, ComposeOptions options,
        List<ComposeAction> actions, List<string> warnings, List<string> errors)
    {
        foreach (var key in root.Keys.Where(k => k is not ("version" or "name" or "services" or "networks" or "volumes") && !k.StartsWith("x-", StringComparison.Ordinal)))
        {
            warnings.Add(key switch
            {
                "secrets" or "configs" => $"Top-level '{key}' isn't supported by WSLc and will be ignored.",
                "include" => "'include' isn't supported; included compose files are not loaded.",
                _ => $"Top-level '{key}' isn't supported by WSLc and will be ignored.",
            });
        }

        var services = Map(root.GetValueOrDefault("services")) ?? [];
        if (services.Count == 0)
        {
            throw new ComposeException("The compose file defines no services.");
        }

        var topNetworks = Map(root.GetValueOrDefault("networks")) ?? [];
        var topVolumes = Map(root.GetValueOrDefault("volumes")) ?? [];

        var networkRefs = new Dictionary<string, NetworkRef>(StringComparer.Ordinal);
        foreach (var (key, value) in topNetworks)
        {
            var def = Map(value) ?? [];
            var external = IsTrue(def.GetValueOrDefault("external"));
            var named = def.GetValueOrDefault("name") as string;
            networkRefs[key] = new NetworkRef(key, external ? named ?? key : named ?? $"{project}_{key}", external);
        }

        var volumeRefs = new Dictionary<string, VolumeRef>(StringComparer.Ordinal);
        foreach (var (key, value) in topVolumes)
        {
            var def = Map(value) ?? [];
            var external = IsTrue(def.GetValueOrDefault("external"));
            var named = def.GetValueOrDefault("name") as string;
            volumeRefs[key] = new VolumeRef(external ? named ?? key : named ?? $"{project}_{key}", external);
        }

        var usedNetworks = new HashSet<string>(StringComparer.Ordinal);
        var runActions = new Dictionary<string, ComposeAction>(StringComparer.Ordinal);
        var buildActions = new List<ComposeAction>();
        var deps = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var healthchecked = services
            .Where(s => Map(s.Value)?.GetValueOrDefault("healthcheck") is not null)
            .Select(s => s.Key)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (name, value) in services)
        {
            var svc = Map(value) ?? throw new ComposeException($"Service '{name}' must be a mapping.");
            var (run, build) = PlanService(project, name, svc, services, options, networkRefs, volumeRefs, usedNetworks, healthchecked, warnings, errors);
            runActions[name] = run;
            if (build is not null)
            {
                buildActions.Add(build);
            }

            deps[name] = run.Waits?.Select(w => w.Service).ToList() ?? [];
        }

        foreach (var net in usedNetworks.Order(StringComparer.Ordinal))
        {
            if (networkRefs.TryGetValue(net, out var r))
            {
                if (r.External)
                {
                    continue;
                }

                actions.Add(CreateNetwork(project, r, Map(topNetworks.GetValueOrDefault(net)) ?? [], warnings));
            }
            else if (net == "default")
            {
                actions.Add(CreateNetwork(project, new NetworkRef("default", $"{project}_default", false), [], warnings));
            }
        }

        foreach (var (key, r) in volumeRefs.Where(v => !v.Value.External))
        {
            actions.Add(CreateVolume(project, r, Map(topVolumes.GetValueOrDefault(key)) ?? [], warnings));
        }

        actions.AddRange(buildActions);
        foreach (var name in TopologicalOrder(services.Keys.ToList(), deps, errors))
        {
            actions.Add(runActions[name]);
        }
    }

    private static ComposeAction CreateNetwork(string project, NetworkRef r, Dictionary<string, object?> def, List<string> warnings)
    {
        var args = new List<string> { "network", "create", "--label", $"{ComposeLabels.Project}={project}" };
        if (def.GetValueOrDefault("driver") is string driver)
        {
            args.AddRange(["--driver", driver]);
        }

        if (IsTrue(def.GetValueOrDefault("internal")))
        {
            args.Add("--internal");
        }

        foreach (var (k, v) in KeyValues(def.GetValueOrDefault("driver_opts")))
        {
            args.AddRange(["--opt", $"{k}={v}"]);
        }

        foreach (var (k, v) in KeyValues(def.GetValueOrDefault("labels")))
        {
            args.AddRange(["--label", v is null ? k : $"{k}={v}"]);
        }

        var configs = List(Map(def.GetValueOrDefault("ipam"))?.GetValueOrDefault("config"));
        if (configs is { Count: > 0 } && Map(configs[0]) is { } cfg)
        {
            if (cfg.GetValueOrDefault("subnet") is string subnet)
            {
                args.AddRange(["--subnet", subnet]);
            }

            if (cfg.GetValueOrDefault("gateway") is string gateway)
            {
                args.AddRange(["--gateway", gateway]);
            }

            if (cfg.GetValueOrDefault("ip_range") is string range)
            {
                args.AddRange(["--ip-range", range]);
            }
        }

        foreach (var key in def.Keys.Where(k => k is not ("driver" or "internal" or "driver_opts" or "labels" or "ipam" or "name" or "external") && !k.StartsWith("x-", StringComparison.Ordinal)))
        {
            warnings.Add($"Network '{r.Key}': '{key}' isn't supported by WSLc and will be ignored.");
        }

        args.Add(r.Actual);
        return new ComposeAction(ActionKind.CreateNetwork, r.Actual, $"Create network {r.Actual}", args);
    }

    private static ComposeAction CreateVolume(string project, VolumeRef r, Dictionary<string, object?> def, List<string> warnings)
    {
        var args = new List<string> { "volume", "create", "--label", $"{ComposeLabels.Project}={project}" };
        if (def.GetValueOrDefault("driver") is string driver && driver != "local")
        {
            args.AddRange(["--driver", driver]);
        }

        foreach (var (k, v) in KeyValues(def.GetValueOrDefault("driver_opts")))
        {
            args.AddRange(["--opt", $"{k}={v}"]);
        }

        foreach (var (k, v) in KeyValues(def.GetValueOrDefault("labels")))
        {
            args.AddRange(["--label", v is null ? k : $"{k}={v}"]);
        }

        foreach (var key in def.Keys.Where(k => k is not ("driver" or "driver_opts" or "labels" or "name" or "external") && !k.StartsWith("x-", StringComparison.Ordinal)))
        {
            warnings.Add($"Volume '{r.Actual}': '{key}' isn't supported by WSLc and will be ignored.");
        }

        args.Add(r.Actual);
        return new ComposeAction(ActionKind.CreateVolume, r.Actual, $"Create volume {r.Actual}", args);
    }

    private static (ComposeAction Run, ComposeAction? Build) PlanService(
        string project, string name, Dictionary<string, object?> svc, Dictionary<string, object?> allServices,
        ComposeOptions options, Dictionary<string, NetworkRef> networkRefs, Dictionary<string, VolumeRef> volumeRefs,
        HashSet<string> usedNetworks, HashSet<string> healthchecked, List<string> warnings, List<string> errors)
    {
        void Warn(string text) => warnings.Add($"Service '{name}': {text}");

        foreach (var key in svc.Keys)
        {
            if (SupportedServiceKeys.Contains(key) || key.StartsWith("x-", StringComparison.Ordinal))
            {
                continue;
            }

            if (key == "restart" && svc[key] as string is "no" or "\"no\"")
            {
                continue;
            }

            Warn(UnsupportedServiceKeys.TryGetValue(key, out var why)
                ? $"'{key}' — {why}."
                : $"'{key}' isn't supported by WSLc and will be ignored.");
        }

        if (Map(svc.GetValueOrDefault("deploy")) is { } deploy && deploy.GetValueOrDefault("replicas") is string replicas && replicas != "1")
        {
            Warn($"deploy.replicas = {replicas} is ignored; only one container is started.");
        }

        var containerName = svc.GetValueOrDefault("container_name") as string ?? $"{project}-{name}";
        var image = svc.GetValueOrDefault("image") as string;
        ComposeAction? build = null;
        if (svc.GetValueOrDefault("build") is { } buildNode)
        {
            image ??= $"{project}-{name}";
            build = PlanBuild(name, image, buildNode, options, warnings, errors);
        }

        if (image is null)
        {
            errors.Add($"Service '{name}' needs an image or a build section.");
            image = string.Empty;
        }

        var args = new List<string>
        {
            "run", "-d", "--name", containerName,
            "--label", $"{ComposeLabels.Project}={project}",
            "--label", $"{ComposeLabels.Service}={name}",
        };

        foreach (var (k, v) in KeyValues(svc.GetValueOrDefault("labels")))
        {
            args.AddRange(["--label", v is null ? k : $"{k}={v}"]);
        }

        if (svc.GetValueOrDefault("pull_policy") is string pull)
        {
            switch (pull)
            {
                case "always": case "missing": case "never":
                    args.AddRange(["--pull", pull]);
                    break;
                case "if_not_present":
                    args.AddRange(["--pull", "missing"]);
                    break;
                default:
                    Warn($"pull_policy '{pull}' isn't supported and will be ignored.");
                    break;
            }
        }

        AddFlag(args, "--hostname", svc.GetValueOrDefault("hostname"));
        AddFlag(args, "--domainname", svc.GetValueOrDefault("domainname"));
        AddFlag(args, "--user", svc.GetValueOrDefault("user"));
        AddFlag(args, "--workdir", svc.GetValueOrDefault("working_dir"));
        AddFlag(args, "--stop-signal", svc.GetValueOrDefault("stop_signal"));
        AddFlag(args, "--shm-size", svc.GetValueOrDefault("shm_size"));
        AddFlag(args, "--cpus", svc.GetValueOrDefault("cpus"));
        if (svc.GetValueOrDefault("mem_limit") is string mem)
        {
            args.AddRange(["--memory", NormalizeMemory(mem)]);
        }

        if (IsTrue(svc.GetValueOrDefault("tty")))
        {
            args.Add("--tty");
        }

        if (IsTrue(svc.GetValueOrDefault("stdin_open")))
        {
            args.Add("--interactive");
        }

        if (svc.GetValueOrDefault("stop_grace_period") is string grace)
        {
            var seconds = ParseDurationSeconds(grace);
            if (seconds is null)
            {
                Warn($"stop_grace_period '{grace}' couldn't be parsed and will be ignored.");
            }
            else
            {
                args.AddRange(["--stop-timeout", seconds.Value.ToString(CultureInfo.InvariantCulture)]);
            }
        }

        foreach (var d in StringList(svc.GetValueOrDefault("dns")))
        {
            args.AddRange(["--dns", d]);
        }

        foreach (var d in StringList(svc.GetValueOrDefault("dns_search")))
        {
            args.AddRange(["--dns-search", d]);
        }

        foreach (var t in StringList(svc.GetValueOrDefault("tmpfs")))
        {
            args.AddRange(["--tmpfs", t]);
        }

        foreach (var (k, v) in KeyValues(svc.GetValueOrDefault("environment")))
        {
            var val = v ?? options.Env.GetValueOrDefault(k);
            if (val is not null)
            {
                args.AddRange(["--env", $"{k}={val}"]);
            }
        }

        foreach (var entry in List(svc.GetValueOrDefault("env_file")) ?? (svc.GetValueOrDefault("env_file") is string single ? [single] : []))
        {
            var (path, required) = entry is string p ? (p, true) : (Map(entry)?.GetValueOrDefault("path") as string ?? string.Empty, !IsFalse(Map(entry)?.GetValueOrDefault("required")));
            var full = Path.GetFullPath(Path.Combine(options.Directory, path));
            if (options.FileExists(full))
            {
                args.AddRange(["--env-file", full]);
            }
            else if (required)
            {
                errors.Add($"Service '{name}': env_file '{path}' wasn't found at {full}.");
            }
        }

        foreach (var port in Ports(svc.GetValueOrDefault("ports")))
        {
            args.AddRange(["--publish", port]);
        }

        foreach (var v in List(svc.GetValueOrDefault("volumes")) ?? [])
        {
            AddVolume(args, name, v, options, volumeRefs, Warn, errors);
        }

        if (svc.GetValueOrDefault("healthcheck") is { } hc)
        {
            AddHealthcheck(args, Map(hc) ?? [], Warn);
        }

        var after = new List<IReadOnlyList<string>>();
        AddNetworks(args, after, project, containerName, name, svc, networkRefs, usedNetworks, Warn, errors);

        var entrypoint = Command(svc.GetValueOrDefault("entrypoint"));
        var command = Command(svc.GetValueOrDefault("command"));
        if (entrypoint.Count > 0)
        {
            args.AddRange(["--entrypoint", entrypoint[0]]);
        }

        args.Add(image);
        args.AddRange(entrypoint.Skip(1));
        args.AddRange(command);

        var waits = new List<ServiceWait>();
        AddDependsOn(name, svc.GetValueOrDefault("depends_on"), allServices, healthchecked, waits, warnings, errors);

        var description = $"Run {name} ({image}) as {containerName}" +
            (waits.Count > 0 ? $", after {string.Join(", ", waits.Select(w => $"{w.Service} ({ConditionText(w.Condition)})"))}" : string.Empty);
        return (new ComposeAction(ActionKind.RunService, containerName, description, args, name, waits, after), build);
    }

    private static string ConditionText(string condition) => condition switch
    {
        "service_healthy" => "healthy",
        "service_completed_successfully" => "completed",
        _ => "started",
    };

    private static ComposeAction? PlanBuild(string name, string image, object node, ComposeOptions options, List<string> warnings, List<string> errors)
    {
        string context;
        string? dockerfile = null;
        string? target = null;
        var buildArgs = new List<(string K, string? V)>();
        if (node is string s)
        {
            context = s;
        }
        else
        {
            var map = Map(node) ?? [];
            context = map.GetValueOrDefault("context") as string ?? ".";
            dockerfile = map.GetValueOrDefault("dockerfile") as string;
            target = map.GetValueOrDefault("target") as string;
            buildArgs = KeyValues(map.GetValueOrDefault("args")).ToList();
            foreach (var key in map.Keys.Where(k => !SupportedBuildKeys.Contains(k) && !k.StartsWith("x-", StringComparison.Ordinal)))
            {
                warnings.Add($"Service '{name}': build.{key} isn't supported by WSLc and will be ignored.");
            }
        }

        if (context.Contains("://") || context.StartsWith("git@", StringComparison.Ordinal))
        {
            errors.Add($"Service '{name}': remote build contexts aren't supported; use a local directory.");
            return null;
        }

        var contextPath = Path.GetFullPath(Path.Combine(options.Directory, context));
        var args = new List<string> { "build", "--tag", image };
        if (dockerfile is not null)
        {
            args.AddRange(["--file", Path.GetFullPath(Path.Combine(contextPath, dockerfile))]);
        }

        if (target is not null)
        {
            args.AddRange(["--target", target]);
        }

        foreach (var (k, v) in buildArgs)
        {
            var val = v ?? options.Env.GetValueOrDefault(k);
            if (val is not null)
            {
                args.AddRange(["--build-arg", $"{k}={val}"]);
            }
        }

        args.Add(contextPath);
        return new ComposeAction(ActionKind.Build, image, $"Build {image} from {contextPath}", args, name);
    }

    private static void AddNetworks(
        List<string> args, List<IReadOnlyList<string>> after, string project, string containerName, string service,
        Dictionary<string, object?> svc, Dictionary<string, NetworkRef> networkRefs, HashSet<string> usedNetworks,
        Action<string> warn, List<string> errors)
    {
        if (svc.GetValueOrDefault("network_mode") is string mode)
        {
            if (mode is "host" or "none" or "bridge")
            {
                args.AddRange(["--network", mode]);
                if (svc.ContainsKey("networks"))
                {
                    warn("'networks' is ignored because network_mode is set.");
                }

                return;
            }

            warn($"network_mode '{mode}' isn't supported; the container joins the project's default network instead.");
        }

        var configs = new List<(string Key, Dictionary<string, object?> Config)>();
        switch (svc.GetValueOrDefault("networks"))
        {
            case List<object?> list:
                configs.AddRange(list.OfType<string>().Select(n => (n, new Dictionary<string, object?>())));
                break;
            case Dictionary<string, object?> map:
                configs.AddRange(map.Select(kv => (kv.Key, Map(kv.Value) ?? [])));
                break;
        }

        if (configs.Count == 0)
        {
            configs.Add(("default", []));
        }

        var first = true;
        foreach (var (key, config) in configs)
        {
            usedNetworks.Add(key);
            string actual;
            if (networkRefs.TryGetValue(key, out var r))
            {
                actual = r.Actual;
            }
            else if (key == "default")
            {
                actual = $"{project}_default";
            }
            else
            {
                errors.Add($"Service '{service}' refers to undefined network '{key}'.");
                continue;
            }

            var aliases = new List<string> { service };
            aliases.AddRange(StringList(config.GetValueOrDefault("aliases")).Where(a => !aliases.Contains(a)));
            var ip = config.GetValueOrDefault("ipv4_address") as string;
            if (first)
            {
                args.AddRange(["--network", actual]);
                foreach (var a in aliases)
                {
                    args.AddRange(["--network-alias", a]);
                }

                if (ip is not null)
                {
                    args.AddRange(["--ip", ip]);
                }

                first = false;
            }
            else
            {
                var connect = new List<string> { "network", "connect" };
                foreach (var a in aliases)
                {
                    connect.AddRange(["--network-alias", a]);
                }

                if (ip is not null)
                {
                    connect.AddRange(["--ip", ip]);
                }

                connect.AddRange([actual, containerName]);
                after.Add(connect);
            }
        }
    }

    private static void AddVolume(
        List<string> args, string service, object? entry, ComposeOptions options,
        Dictionary<string, VolumeRef> volumeRefs, Action<string> warn, List<string> errors)
    {
        string? source = null, target = null;
        var readOnly = false;
        var type = string.Empty;
        if (entry is string text)
        {
            (source, target, readOnly) = SplitVolume(text);
            if (target is null)
            {
                target = source;
                source = null;
            }

            type = source is null ? "anonymous" : IsPath(source) ? "bind" : "volume";
        }
        else if (Map(entry) is { } map)
        {
            type = map.GetValueOrDefault("type") as string ?? "volume";
            source = map.GetValueOrDefault("source") as string;
            target = map.GetValueOrDefault("target") as string;
            readOnly = IsTrue(map.GetValueOrDefault("read_only"));
            foreach (var key in map.Keys.Where(k => k is not ("type" or "source" or "target" or "read_only") && !k.StartsWith("x-", StringComparison.Ordinal)))
            {
                warn($"volumes entry option '{key}' isn't supported and will be ignored.");
            }
        }

        if (string.IsNullOrEmpty(target))
        {
            errors.Add($"Service '{service}' has a volume entry without a target.");
            return;
        }

        if (type == "tmpfs")
        {
            args.AddRange(["--tmpfs", target]);
            return;
        }

        if (string.IsNullOrEmpty(source))
        {
            args.AddRange(["--volume", target]);
            return;
        }

        if (type == "bind" || IsPath(source))
        {
            source = ResolveHostPath(source, options.Directory);
        }
        else if (volumeRefs.TryGetValue(source, out var vr))
        {
            source = vr.Actual;
        }
        else
        {
            errors.Add($"Service '{service}' refers to undefined volume '{source}'.");
            return;
        }

        args.AddRange(["--volume", $"{source}:{target}{(readOnly ? ":ro" : string.Empty)}"]);
    }

    private static string ResolveHostPath(string path, string directory)
    {
        if (path.StartsWith('~'))
        {
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.TrimStart('~', '/', '\\'));
        }

        return Path.GetFullPath(Path.Combine(directory, path));
    }

    private static bool IsPath(string source) =>
        source.StartsWith('.') || source.StartsWith('/') || source.StartsWith('\\') || source.StartsWith('~')
        || (source.Length >= 2 && char.IsAsciiLetter(source[0]) && source[1] == ':');

    /// <summary>Splits "src:target[:mode]" while keeping a Windows drive letter ("C:\x") inside the source.</summary>
    public static (string Source, string? Target, bool ReadOnly) SplitVolume(string text)
    {
        var prefix = 0;
        if (text.Length >= 3 && char.IsAsciiLetter(text[0]) && text[1] == ':' && (text[2] is '\\' or '/'))
        {
            prefix = 2;
        }

        var parts = new List<string>();
        var start = 0;
        for (var i = prefix; i < text.Length; i++)
        {
            if (text[i] == ':')
            {
                parts.Add(text[start..i]);
                start = i + 1;
            }
        }

        parts.Add(text[start..]);
        if (parts.Count == 1)
        {
            return (parts[0], null, false);
        }

        var readOnly = parts.Count > 2 && parts[2].Split(',').Contains("ro");
        return (parts[0], parts[1], readOnly);
    }

    private static IEnumerable<string> Ports(object? node)
    {
        foreach (var p in List(node) ?? [])
        {
            if (p is string s)
            {
                yield return s;
            }
            else if (Map(p) is { } m && m.GetValueOrDefault("target") is string target)
            {
                var published = m.GetValueOrDefault("published") as string;
                var host = m.GetValueOrDefault("host_ip") as string;
                var proto = m.GetValueOrDefault("protocol") as string;
                var spec = published is null ? target : $"{published}:{target}";
                if (host is not null && published is not null)
                {
                    spec = $"{host}:{spec}";
                }

                yield return proto is null or "tcp" ? spec : $"{spec}/{proto}";
            }
        }
    }

    private static void AddHealthcheck(List<string> args, Dictionary<string, object?> hc, Action<string> warn)
    {
        var test = hc.GetValueOrDefault("test");
        if (IsTrue(hc.GetValueOrDefault("disable")) || test is List<object?> { Count: > 0 } l && l[0] as string == "NONE")
        {
            args.Add("--no-healthcheck");
            return;
        }

        string? cmd = test switch
        {
            string s => s,
            List<object?> { Count: > 0 } list when list[0] as string == "CMD-SHELL" => string.Join(' ', list.Skip(1).OfType<string>()),
            List<object?> { Count: > 0 } list when list[0] as string == "CMD" => string.Join(' ', list.Skip(1).OfType<string>().Select(Quote)),
            List<object?> list => string.Join(' ', list.OfType<string>().Select(Quote)),
            _ => null,
        };

        if (!string.IsNullOrWhiteSpace(cmd))
        {
            args.AddRange(["--health-cmd", cmd]);
        }

        AddFlag(args, "--health-interval", hc.GetValueOrDefault("interval"));
        AddFlag(args, "--health-timeout", hc.GetValueOrDefault("timeout"));
        AddFlag(args, "--health-retries", hc.GetValueOrDefault("retries"));
        AddFlag(args, "--health-start-period", hc.GetValueOrDefault("start_period"));
        if (hc.ContainsKey("start_interval"))
        {
            warn("healthcheck.start_interval isn't supported and will be ignored.");
        }
    }

    private static void AddDependsOn(
        string service, object? node, Dictionary<string, object?> allServices, HashSet<string> healthchecked,
        List<ServiceWait> waits, List<string> warnings, List<string> errors)
    {
        var entries = node switch
        {
            List<object?> list => list.OfType<string>().Select(n => (n, "service_started", true)).ToList(),
            Dictionary<string, object?> map => map.Select(kv =>
            {
                var m = Map(kv.Value);
                return (kv.Key, m?.GetValueOrDefault("condition") as string ?? "service_started", !IsFalse(m?.GetValueOrDefault("required")));
            }).ToList(),
            _ => [],
        };

        foreach (var (dep, condition, required) in entries)
        {
            if (!allServices.ContainsKey(dep))
            {
                if (required)
                {
                    errors.Add($"Service '{service}' depends on undefined service '{dep}'.");
                }

                continue;
            }

            var effective = condition;
            if (condition == "service_healthy" && !healthchecked.Contains(dep))
            {
                warnings.Add($"Service '{service}': depends_on '{dep}' waits for healthy, but '{dep}' has no healthcheck; it will only wait for the container to start.");
                effective = "service_started";
            }
            else if (condition is not ("service_started" or "service_healthy" or "service_completed_successfully"))
            {
                warnings.Add($"Service '{service}': depends_on condition '{condition}' isn't supported; treating it as service_started.");
                effective = "service_started";
            }

            waits.Add(new ServiceWait(dep, effective));
        }
    }

    public static IReadOnlyList<string> TopologicalOrder(IReadOnlyList<string> names, Dictionary<string, List<string>> deps, List<string> errors)
    {
        var order = new List<string>();
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var cyclic = false;

        void Visit(string n)
        {
            if (state.GetValueOrDefault(n) == 2)
            {
                return;
            }

            if (state.GetValueOrDefault(n) == 1)
            {
                cyclic = true;
                return;
            }

            state[n] = 1;
            foreach (var d in deps.GetValueOrDefault(n) ?? [])
            {
                Visit(d);
            }

            state[n] = 2;
            order.Add(n);
        }

        foreach (var n in names)
        {
            Visit(n);
        }

        if (cyclic)
        {
            errors.Add("The services' depends_on entries form a cycle.");
        }

        return order;
    }

    private static string Quote(string arg) =>
        arg.Length > 0 && arg.IndexOfAny([' ', '"', '\'', '\t']) < 0 ? arg : "\"" + arg.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string NormalizeMemory(string mem)
    {
        var m = MemoryRegex().Match(mem.Trim());
        if (!m.Success)
        {
            return mem;
        }

        var unit = m.Groups[2].Value.ToUpperInvariant().TrimEnd('B');
        return m.Groups[1].Value + unit;
    }

    public static int? ParseDurationSeconds(string text)
    {
        var total = 0.0;
        var matches = DurationRegex().Matches(text.Trim());
        if (matches.Count == 0 || string.Concat(matches.Select(m => m.Value)) != text.Trim())
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var plain) ? (int)Math.Ceiling(plain) : null;
        }

        foreach (Match m in matches)
        {
            var n = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            total += m.Groups[2].Value switch
            {
                "h" => n * 3600,
                "m" => n * 60,
                "s" => n,
                "ms" => n / 1000,
                _ => 0,
            };
        }

        return (int)Math.Ceiling(total);
    }

    /// <summary>A command or entrypoint is either a list (used verbatim) or a string split like a shell would.</summary>
    private static IReadOnlyList<string> Command(object? node) => node switch
    {
        string s => ShellSplit(s),
        List<object?> list => list.Select(x => x as string ?? string.Empty).ToList(),
        _ => [],
    };

    public static IReadOnlyList<string> ShellSplit(string text)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inToken = false;
        char quote = '\0';
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
                else if (c == '\\' && quote == '"' && i + 1 < text.Length && text[i + 1] is '"' or '\\')
                {
                    sb.Append(text[++i]);
                }
                else
                {
                    sb.Append(c);
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
                inToken = true;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (inToken)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                    inToken = false;
                }
            }
            else
            {
                sb.Append(c);
                inToken = true;
            }
        }

        if (inToken)
        {
            result.Add(sb.ToString());
        }

        return result;
    }

    private static void AddFlag(List<string> args, string flag, object? value)
    {
        if (value is string s && s.Length > 0)
        {
            args.AddRange([flag, s]);
        }
    }

    private static Dictionary<string, object?>? Map(object? o) => o as Dictionary<string, object?>;

    private static List<object?>? List(object? o) => o as List<object?>;

    private static IEnumerable<string> StringList(object? o) => o switch
    {
        string s => [s],
        List<object?> l => l.OfType<string>(),
        _ => [],
    };

    /// <summary>Reads a mapping or a list of "KEY=VALUE" / "KEY" entries; a null value means "take it from the environment".</summary>
    private static IEnumerable<(string Key, string? Value)> KeyValues(object? o)
    {
        switch (o)
        {
            case Dictionary<string, object?> map:
                foreach (var (k, v) in map)
                {
                    yield return (k, v as string);
                }

                break;
            case List<object?> list:
                foreach (var item in list.OfType<string>())
                {
                    var eq = item.IndexOf('=');
                    yield return eq < 0 ? (item, null) : (item[..eq], item[(eq + 1)..]);
                }

                break;
        }
    }

    private static bool IsTrue(object? o) => o is string s && s.Equals("true", StringComparison.OrdinalIgnoreCase);

    private static bool IsFalse(object? o) => o is string s && s.Equals("false", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("[^a-z0-9_-]")]
    private static partial Regex InvalidNameChars();

    [GeneratedRegex(@"^(\d+(?:\.\d+)?)\s*([a-zA-Z]*)$")]
    private static partial Regex MemoryRegex();

    [GeneratedRegex(@"(\d+(?:\.\d+)?)(ms|h|m|s)")]
    private static partial Regex DurationRegex();
}
