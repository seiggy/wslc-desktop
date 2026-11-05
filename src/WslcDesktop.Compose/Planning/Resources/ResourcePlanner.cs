using WslcDesktop.Localization;
using WslcDesktop.Compose.Planning.Services;
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

namespace WslcDesktop.Compose.Planning.Resources;

/// <summary>Plans networks and volumes, and attaches them to services.</summary>
internal static class ResourcePlanner
{
    internal static ComposeAction CreateNetwork(PlanContext context, NetworkRef r, Dictionary<string, object?> def)
    {
        var args = new List<string> { "network", "create", "--label", $"{ComposeLabels.Project}={context.Project}" };
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
            context.Warnings.Add(UiStrings.Format("NetworkIsnTSupportedByWSLcAndWillBe", r.Key, key));
        }

        args.Add(r.Actual);
        return new ComposeAction(ActionKind.CreateNetwork, r.Actual, UiStrings.Format("CreateNetwork", r.Actual), args);
    }

    internal static ComposeAction CreateVolume(PlanContext context, VolumeRef r, Dictionary<string, object?> def)
    {
        var args = new List<string> { "volume", "create", "--label", $"{ComposeLabels.Project}={context.Project}" };
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
            context.Warnings.Add(UiStrings.Format("VolumeIsnTSupportedByWSLcAndWillBe", r.Actual, key));
        }

        args.Add(r.Actual);
        return new ComposeAction(ActionKind.CreateVolume, r.Actual, UiStrings.Format("CreateVolume", r.Actual), args);
    }

    internal static void AddNetworks(
        PlanContext context, List<string> args, List<IReadOnlyList<string>> after, string containerName, string service,
        Dictionary<string, object?> svc, Action<string> warn)
    {
        if (svc.GetValueOrDefault("network_mode") is string mode)
        {
            if (mode is "host" or "none" or "bridge")
            {
                args.AddRange(["--network", mode]);
                if (svc.ContainsKey("networks"))
                {
                    warn(UiStrings.Get("NetworksIsIgnoredBecauseNetworkModeIsSet"));
                }

                return;
            }

            warn(UiStrings.Format("NetworkModeIsnTSupportedTheContainerJoinsThe", mode));
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
            context.UsedNetworks.Add(key);
            string actual;
            if (context.Networks.TryGetValue(key, out var r))
            {
                actual = r.Actual;
            }
            else if (key == "default")
            {
                actual = $"{context.Project}_default";
            }
            else
            {
                context.Errors.Add(UiStrings.Format("ServiceRefersToUndefinedNetwork", service, key));
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

    internal static void AddVolume(
        PlanContext context, List<string> args, string service, object? entry, Action<string> warn)
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
                warn(UiStrings.Format("VolumesEntryOptionIsnTSupportedAndWillBe", key));
            }
        }

        if (string.IsNullOrEmpty(target))
        {
            context.Errors.Add(UiStrings.Format("ServiceHasAVolumeEntryWithoutATarget", service));
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
            source = ResolveHostPath(source, context.Options.Directory);
        }
        else if (context.Volumes.TryGetValue(source, out var vr))
        {
            source = vr.Actual;
        }
        else
        {
            context.Errors.Add(UiStrings.Format("ServiceRefersToUndefinedVolume", service, source));
            return;
        }

        args.AddRange(["--volume", $"{source}:{target}{(readOnly ? ":ro" : string.Empty)}"]);
    }
}
