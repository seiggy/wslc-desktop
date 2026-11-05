using WslcDesktop.Localization;
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
                    errors.Add(UiStrings.Format("SecretsCanOnlyBeUsedInEnvironmentVariableValues2", action.Description));
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
        return name.Length > 0 ? name : throw new ComposeException(UiStrings.Get("CouldnTDeriveAProjectNameSetAName"));
    }

    private static void Build(
        Dictionary<string, object?> root, string project, ComposeOptions options,
        List<ComposeAction> actions, List<string> warnings, List<string> errors, Dictionary<string, SecretImportSource> imports)
    {
        var context = new PlanContext(project, options, Map(root.GetValueOrDefault("services")) ?? [], warnings, errors);
        foreach (var key in root.Keys.Where(k => k is not ("version" or "name" or "services" or "networks" or "volumes" or "secrets") && !k.StartsWith("x-", StringComparison.Ordinal)))
        {
            context.Warnings.Add(key switch
            {
                "configs" => UiStrings.Format("TopLevelIsnTSupportedByWSLcAndWill", key),
                "include" => UiStrings.Get("IncludeIsnTSupportedIncludedComposeFilesAreNot"),
                _ => UiStrings.Format("TopLevelIsnTSupportedByWSLcAndWill", key),
            });
        }

        // A top-level secret without file/environment/content is a reference to a stored secret (by "name:", else its key).
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
                context.Errors.Add(UiStrings.Format("SecretMapsToWhichIsnTAValidStored", key, id));
                continue;
            }

            context.Secrets[key] = id;
            if (hasSource)
            {
                imports[id] = new SecretImportSource(
                    fromFile is null ? null : Path.GetFullPath(Path.Combine(options.Directory, fromFile)),
                    fromEnv,
                    fromContent);
            }
        }

        if (context.Services.Count == 0)
        {
            throw new ComposeException(UiStrings.Get("TheComposeFileDefinesNoServices"));
        }

        var topNetworks = Map(root.GetValueOrDefault("networks")) ?? [];
        var topVolumes = Map(root.GetValueOrDefault("volumes")) ?? [];

        foreach (var (key, value) in topNetworks)
        {
            var def = Map(value) ?? [];
            var external = IsTrue(def.GetValueOrDefault("external"));
            var named = def.GetValueOrDefault("name") as string;
            context.Networks[key] = new NetworkRef(key, external ? named ?? key : named ?? $"{project}_{key}", external);
        }

        foreach (var (key, value) in topVolumes)
        {
            var def = Map(value) ?? [];
            var external = IsTrue(def.GetValueOrDefault("external"));
            var named = def.GetValueOrDefault("name") as string;
            context.Volumes[key] = new VolumeRef(external ? named ?? key : named ?? $"{project}_{key}", external);
        }

        var runActions = new Dictionary<string, ComposeAction>(StringComparer.Ordinal);
        var buildActions = new List<ComposeAction>();
        var deps = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var (name, value) in context.Services)
        {
            var svc = Map(value) ?? throw new ComposeException(UiStrings.Format("ServiceMustBeAMapping", name));
            var (run, build) = PlanService(context, name, svc);
            runActions[name] = run;
            if (build is not null)
            {
                buildActions.Add(build);
            }

            deps[name] = run.Waits?.Select(w => w.Service).ToList() ?? [];
        }

        foreach (var net in context.UsedNetworks.Order(StringComparer.Ordinal))
        {
            if (context.Networks.TryGetValue(net, out var r))
            {
                if (r.External)
                {
                    continue;
                }

                actions.Add(CreateNetwork(context, r, Map(topNetworks.GetValueOrDefault(net)) ?? []));
            }
            else if (net == "default")
            {
                actions.Add(CreateNetwork(context, new NetworkRef("default", $"{project}_default", false), []));
            }
        }

        foreach (var (key, r) in context.Volumes.Where(v => !v.Value.External))
        {
            actions.Add(CreateVolume(context, r, Map(topVolumes.GetValueOrDefault(key)) ?? []));
        }

        actions.AddRange(buildActions);
        foreach (var name in TopologicalOrder(context.Services.Keys.ToList(), deps, context.Errors))
        {
            actions.Add(runActions[name]);
        }
    }

    [GeneratedRegex("[^a-z0-9_-]")]
    private static partial Regex InvalidNameChars();
}
