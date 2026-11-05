using WslcDesktop.Localization;
using WslcDesktop.Compose.Planning.Resources;
using WslcDesktop.Compose.Planning;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using WslcDesktop.Compose.Execution;
using WslcDesktop.Compose.Parsing;
using WslcDesktop.Compose.Secrets;
using WslcDesktop.Compose.Secrets.Injection;
using static WslcDesktop.Compose.Parsing.ComposeValues;
using static WslcDesktop.Compose.Parsing.ComposeUnits;
using static WslcDesktop.Compose.Planning.Resources.VolumeSpec;
using static WslcDesktop.Compose.Planning.Resources.ResourcePlanner;

namespace WslcDesktop.Compose.Planning.Services;

/// <summary>Turns one compose service into a run (and optional build) action.</summary>
internal static partial class ServicePlanner
{
    internal static readonly HashSet<string> SupportedServiceKeys =
    [
        "image", "build", "command", "entrypoint", "environment", "env_file", "working_dir", "user", "ports", "expose",
        "volumes", "networks", "network_mode", "healthcheck", "mem_limit", "cpus", "depends_on", "container_name",
        "hostname", "domainname", "labels", "stop_signal", "stop_grace_period", "tty", "stdin_open", "shm_size",
        "tmpfs", "dns", "dns_search", "pull_policy", "secrets",
    ];

    internal static Dictionary<string, string> UnsupportedServiceKeys => new(StringComparer.Ordinal)
    {
        ["deploy"] = UiStrings.Get("DeployReplicasResourcesPlacementRestartPolicyIsnTSupported"),
        ["profiles"] = UiStrings.Get("ProfilesArenTSupportedThisServiceIsAlwaysStarted"),
        ["configs"] = UiStrings.Get("ConfigsArenTSupportedAndWonTBeMounted"),
        ["extends"] = UiStrings.Get("ExtendsIsnTSupportedTheServiceIsNotMerged"),
        ["links"] = UiStrings.Get("LinksArenTSupportedUseNetworksAndServiceNames"),
        ["platform"] = UiStrings.Get("PlatformSelectionIsnTSupported"),
        ["scale"] = UiStrings.Get("ScaleIsnTSupportedASingleContainerIsStarted"),
    };

    internal static readonly HashSet<string> SupportedBuildKeys = ["context", "dockerfile", "args", "target"];

    internal static (ComposeAction Run, ComposeAction? Build) PlanService(
        PlanContext context, string name, Dictionary<string, object?> svc)
    {
        void Warn(string text) => context.Warnings.Add(UiStrings.Format("Service", name, text));

        foreach (var key in svc.Keys)
        {
            if (SupportedServiceKeys.Contains(key) || key.StartsWith("x-", StringComparison.Ordinal))
            {
                continue;
            }

            // Handled below: restart maps to the app-level restart policy.
            if (key == "restart")
            {
                continue;
            }

            Warn(UnsupportedServiceKeys.TryGetValue(key, out var why)
                ? UiStrings.Format("ComposeFieldWarning", key, why)
                : UiStrings.Format("IsnTSupportedByWSLcAndWillBeIgnored", key));
        }

        if (Map(svc.GetValueOrDefault("deploy")) is { } deploy && deploy.GetValueOrDefault("replicas") is string replicas && replicas != "1")
        {
            Warn(UiStrings.Format("DeployReplicasIsIgnoredOnlyOneContainerIsStarted", replicas));
        }

        var containerName = svc.GetValueOrDefault("container_name") as string ?? $"{context.Project}-{name}";
        var image = svc.GetValueOrDefault("image") as string;
        ComposeAction? build = null;
        if (svc.GetValueOrDefault("build") is { } buildNode)
        {
            image ??= $"{context.Project}-{name}";
            build = PlanBuild(context, name, image, buildNode);
        }

        if (image is null)
        {
            context.Errors.Add(UiStrings.Format("ServiceNeedsAnImageOrABuildSection", name));
            image = string.Empty;
        }

        var args = new List<string>
        {
            "run", "-d", "--name", containerName,
            "--label", $"{ComposeLabels.Project}={context.Project}",
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
                    Warn(UiStrings.Format("PullPolicyIsnTSupportedAndWillBeIgnored", pull));
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
                Warn(UiStrings.Format("StopGracePeriodCouldnTBeParsedAndWill", grace));
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

        var explicitEnv = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (k, v) in KeyValues(svc.GetValueOrDefault("environment")))
        {
            var val = v ?? context.Options.Env.GetValueOrDefault(k);
            if (val is not null)
            {
                args.AddRange(["--env", $"{k}={val}"]);
                explicitEnv.Add(k);
            }
        }

        // WSLc has no secret mounts, so a compose secret is delivered as an environment variable named after it.
        foreach (var entry in List(svc.GetValueOrDefault("secrets")) ?? [])
        {
            var source = entry as string ?? Map(entry)?.GetValueOrDefault("source") as string;
            var target = Map(entry)?.GetValueOrDefault("target") as string ?? source;
            if (source is null || target is null)
            {
                Warn(UiStrings.Get("ASecretsEntryHasNoSourceAndWasIgnored"));
            }
            else if (!context.Secrets.TryGetValue(source, out var id))
            {
                context.Errors.Add(UiStrings.Format("ServiceUsesSecretWhichIsnTDefinedOrIsn", name, source));
            }
            else
            {
                var envName = SecretEnvName().Replace(Path.GetFileName(target).ToUpperInvariant(), "_");
                if (explicitEnv.Add(envName))
                {
                    args.AddRange(["--env", $"{envName}={SecretTokens.Format(id)}"]);
                }
            }
        }

        foreach (var entry in List(svc.GetValueOrDefault("env_file")) ?? (svc.GetValueOrDefault("env_file") is string single ? [single] : []))
        {
            var (path, required) = entry is string p ? (p, true) : (Map(entry)?.GetValueOrDefault("path") as string ?? string.Empty, !IsFalse(Map(entry)?.GetValueOrDefault("required")));
            var full = Path.GetFullPath(Path.Combine(context.Options.Directory, path));
            if (context.Options.FileExists(full))
            {
                args.AddRange(["--env-file", full]);
            }
            else if (required)
            {
                context.Errors.Add(UiStrings.Format("ServiceEnvFileWasnTFoundAt", name, path, full));
            }
        }

        foreach (var port in Ports(svc.GetValueOrDefault("ports")))
        {
            args.AddRange(["--publish", port]);
        }

        foreach (var v in List(svc.GetValueOrDefault("volumes")) ?? [])
        {
            AddVolume(context, args, name, v, Warn);
        }

        if (svc.GetValueOrDefault("healthcheck") is { } hc)
        {
            AddHealthcheck(args, Map(hc) ?? [], Warn);
        }

        var after = new List<IReadOnlyList<string>>();
        AddNetworks(context, args, after, containerName, name, svc, Warn);

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
        AddDependsOn(context, name, svc.GetValueOrDefault("depends_on"), waits);

        var description = UiStrings.Format("RunAs", name, image, containerName) +
            (waits.Count > 0 ? UiStrings.Format("After", string.Join(", ", waits.Select(w => $"{w.Service} ({ConditionText(w.Condition)})"))) : string.Empty);
        var restart = RestartValue(svc.GetValueOrDefault("restart"), Warn);
        return (new ComposeAction(ActionKind.RunService, containerName, description, args, name, waits, after, restart), build);
    }

    // Compose's restart values map 1:1 to the app's restart policy; YAML may read an unquoted `no` as false.
    internal static string? RestartValue(object? node, Action<string> warn)
    {
        var text = (node as string)?.Trim().Trim('"', '\'');
        if (string.IsNullOrEmpty(text) || text is "no" or "false")
        {
            return null;
        }

        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^(always|unless-stopped|on-failure(:\d+)?)$"))
        {
            return text;
        }

        warn(UiStrings.Format("RestartIsnTASupportedRestartPolicyAndWill", text));
        return null;
    }

    internal static string ConditionText(string condition) => condition switch
    {
        "service_healthy" => "healthy",
        "service_completed_successfully" => "completed",
        _ => "started",
    };

    internal static ComposeAction? PlanBuild(PlanContext plan, string name, string image, object node)
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
                plan.Warnings.Add(UiStrings.Format("ServiceBuildIsnTSupportedByWSLcAndWill", name, key));
            }
        }

        if (context.Contains("://") || context.StartsWith("git@", StringComparison.Ordinal))
        {
            plan.Errors.Add(UiStrings.Format("ServiceRemoteBuildContextsArenTSupportedUseA", name));
            return null;
        }

        var contextPath = Path.GetFullPath(Path.Combine(plan.Options.Directory, context));
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
            var val = v ?? plan.Options.Env.GetValueOrDefault(k);
            if (val is not null)
            {
                args.AddRange(["--build-arg", $"{k}={val}"]);
            }
        }

        args.Add(contextPath);
        return new ComposeAction(ActionKind.Build, image, UiStrings.Format("BuildFrom", image, contextPath), args, name);
    }

    internal static IEnumerable<string> Ports(object? node)
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

    internal static void AddHealthcheck(List<string> args, Dictionary<string, object?> hc, Action<string> warn)
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
            warn(UiStrings.Get("HealthcheckStartIntervalIsnTSupportedAndWillBe"));
        }
    }

    internal static void AddDependsOn(
        PlanContext context, string service, object? node, List<ServiceWait> waits)
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
            if (!context.Services.ContainsKey(dep))
            {
                if (required)
                {
                    context.Errors.Add(UiStrings.Format("ServiceDependsOnUndefinedService", service, dep));
                }

                continue;
            }

            var effective = condition;
            if (condition == "service_healthy" && !context.HealthcheckedServices.Contains(dep))
            {
                context.Warnings.Add(UiStrings.Format("ServiceDependsOnWaitsForHealthyButHasNo", service, dep, dep));
                effective = "service_started";
            }
            else if (condition is not ("service_started" or "service_healthy" or "service_completed_successfully"))
            {
                context.Warnings.Add(UiStrings.Format("ServiceDependsOnConditionIsnTSupportedTreatingIt", service, condition));
                effective = "service_started";
            }

            waits.Add(new ServiceWait(dep, effective));
        }
    }

    [GeneratedRegex("[^A-Z0-9_]")]
    internal static partial Regex SecretEnvName();
}
