using WslcDesktop.Compose.Planning.Services;
using WslcDesktop.Compose.Planning.Resources;
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
using static WslcDesktop.Compose.Planning.Services.DependencyOrder;
using static WslcDesktop.Compose.Planning.Resources.ResourcePlanner;
using static WslcDesktop.Compose.Planning.Services.ServicePlanner;

namespace WslcDesktop.Compose.Planning;

/// <summary>Translates a compose file into an ordered list of wslc invocations plus warnings for anything WSLc can't do.</summary>
public static partial class ComposePlanner
{
    public static ComposePlan Plan(string yaml, ComposeOptions options)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var project = string.Empty;
        var actions = new List<ComposeAction>();
        IReadOnlyList<string> secretRefs = [];
        IReadOnlyList<string> missing = [];
        IReadOnlyList<SecretNeed> needs = [];
        try
        {
            var root = YamlTree.ParseDocument(yaml);
            root = (Dictionary<string, object?>)Interpolator.ApplyTree(root, options.Env)!;
            project = ProjectName(root, options);
            var imports = new Dictionary<string, SecretImportSource>(StringComparer.Ordinal);
            Build(root, project, options, actions, warnings, errors, imports);
            (secretRefs, missing, needs) = CheckSecrets(actions, options, errors, imports);
        }
        catch (ComposeException e)
        {
            errors.Add(e.Message);
        }

        return new ComposePlan(project, actions, warnings, errors) { SecretRefs = secretRefs, MissingSecrets = missing, SecretNeeds = needs };
    }

    /// <summary>Secret tokens may only appear as environment values; collects the referenced names, their users, and which aren't stored.</summary>
    private static (IReadOnlyList<string> Refs, IReadOnlyList<string> Missing, IReadOnlyList<SecretNeed> Needs) CheckSecrets(
        List<ComposeAction> actions, ComposeOptions options, List<string> errors, Dictionary<string, SecretImportSource> imports)
    {
        var refs = new List<string>();
        var users = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var action in actions)
        {
            for (var i = 0; i < action.Args.Count; i++)
            {
                var arg = action.Args[i];
                if (!SecretTokens.Contains(arg))
                {
                    continue;
                }

                if (action.Kind == ActionKind.RunService && i > 0 && action.Args[i - 1] == "--env")
                {
                    foreach (var n in SecretTokens.Find(arg))
                    {
                        if (!refs.Contains(n))
                        {
                            refs.Add(n);
                        }

                        if (!users.TryGetValue(n, out var list))
                        {
                            users[n] = list = [];
                        }

                        if (action.Service is { } svc && !list.Contains(svc))
                        {
                            list.Add(svc);
                        }
                    }
                }
                else
                {
                    errors.Add($"{action.Description}: secrets can only be used in environment variable values.");
                }
            }
        }

        var missing = options.SecretExists is null ? [] : refs.Where(n => !options.SecretExists(n)).ToList();
        var needs = refs
            .Select(n => new SecretNeed(n, users.GetValueOrDefault(n) ?? [], !missing.Contains(n), imports.GetValueOrDefault(n)))
            .ToList();
        return (refs, missing, needs);
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

    private static void Build(
        Dictionary<string, object?> root, string project, ComposeOptions options,
        List<ComposeAction> actions, List<string> warnings, List<string> errors, Dictionary<string, SecretImportSource> imports)
    {
        foreach (var key in root.Keys.Where(k => k is not ("version" or "name" or "services" or "networks" or "volumes" or "secrets") && !k.StartsWith("x-", StringComparison.Ordinal)))
        {
            warnings.Add(key switch
            {
                "configs" => $"Top-level '{key}' isn't supported by WSLc and will be ignored.",
                "include" => "'include' isn't supported; included compose files are not loaded.",
                _ => $"Top-level '{key}' isn't supported by WSLc and will be ignored.",
            });
        }

        // A top-level secret without file/environment/content is a reference to a stored secret (by "name:", else its key).
        var secretMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in Map(root.GetValueOrDefault("secrets")) ?? [])
        {
            var def = Map(value) ?? [];
            var fromFile = def.GetValueOrDefault("file") as string;
            var fromEnv = def.GetValueOrDefault("environment") as string;
            var fromContent = def.GetValueOrDefault("content") as string;
            var hasSource = fromFile is not null || fromEnv is not null || fromContent is not null;

            // A file/environment/content secret is stored under its key (a "name:" there means something else in compose).
            var id = hasSource ? key : def.GetValueOrDefault("name") as string ?? key;
            if (!SecretTokens.IsValidName(id))
            {
                errors.Add($"Secret '{key}' maps to '{id}', which isn't a valid stored secret name.");
                continue;
            }

            secretMap[key] = id;
            if (hasSource)
            {
                imports[id] = new SecretImportSource(
                    fromFile is null ? null : Path.GetFullPath(Path.Combine(options.Directory, fromFile)),
                    fromEnv,
                    fromContent);
            }
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
            var (run, build) = PlanService(project, name, svc, services, options, networkRefs, volumeRefs, usedNetworks, healthchecked, secretMap, warnings, errors);
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

    [GeneratedRegex("[^a-z0-9_-]")]
    private static partial Regex InvalidNameChars();
}
