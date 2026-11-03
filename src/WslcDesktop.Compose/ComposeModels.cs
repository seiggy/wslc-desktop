namespace WslcDesktop.Compose;

public static class ComposeLabels
{
    public const string Project = "com.wslc.compose.project";

    public const string Service = "com.wslc.compose.service";

    /// <summary>Extracts the compose project and service from the comma-joined label string that `wslc list` prints.</summary>
    public static bool TryParse(string? labels, out string project, out string service)
    {
        project = service = string.Empty;
        if (string.IsNullOrEmpty(labels))
        {
            return false;
        }

        foreach (var part in labels.Split(','))
        {
            var eq = part.IndexOf('=');
            if (eq < 0)
            {
                continue;
            }

            var key = part[..eq].Trim();
            if (key == Project)
            {
                project = part[(eq + 1)..].Trim();
            }
            else if (key == Service)
            {
                service = part[(eq + 1)..].Trim();
            }
        }

        return project.Length > 0;
    }
}

public enum ActionKind
{
    CreateNetwork,
    CreateVolume,
    Build,
    RunService,
}

public sealed record ServiceWait(string Service, string Condition);

/// <summary>One planned wslc invocation. <see cref="Args"/> excludes the --session option.</summary>
public sealed record ComposeAction(
    ActionKind Kind,
    string Target,
    string Description,
    IReadOnlyList<string> Args,
    string? Service = null,
    IReadOnlyList<ServiceWait>? Waits = null,
    IReadOnlyList<IReadOnlyList<string>>? After = null,
    string? Restart = null);

/// <summary>Where a compose top-level secret's value could be imported from (<c>file:</c>, <c>environment:</c> or <c>content:</c>).</summary>
public sealed record SecretImportSource(string? FilePath = null, string? EnvVar = null, string? Content = null)
{
    public string Describe() => FilePath is not null ? Path.GetFileName(FilePath) : EnvVar is not null ? $"${EnvVar}" : "the compose file";
}

/// <summary>A secret the compose file needs: who uses it, whether it is already stored, and any value the file itself can supply.</summary>
public sealed record SecretNeed(string Name, IReadOnlyList<string> Services, bool Exists, SecretImportSource? Import = null);

public sealed record ComposePlan(
    string Project,
    IReadOnlyList<ComposeAction> Actions,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    /// <summary>Stored secrets the plan references through <c>${secret:NAME}</c> tokens or compose <c>secrets:</c>.</summary>
    public IReadOnlyList<string> SecretRefs { get; init; } = [];

    /// <summary>Referenced secrets that the store doesn't have; the stack can't run until they're added.</summary>
    public IReadOnlyList<string> MissingSecrets { get; init; } = [];

    /// <summary>Every referenced secret with its users and stored state (a superset of <see cref="MissingSecrets"/>).</summary>
    public IReadOnlyList<SecretNeed> SecretNeeds { get; init; } = [];

    public bool CanRun => Errors.Count == 0 && Actions.Count > 0 && MissingSecrets.Count == 0;
}

public sealed class ComposeOptions
{
    /// <summary>Overrides the project name; defaults to the top-level "name" or the directory name.</summary>
    public string? ProjectName { get; init; }

    /// <summary>Directory containing the compose file; relative paths resolve against it.</summary>
    public string Directory { get; init; } = Environment.CurrentDirectory;

    /// <summary>Variables available to ${VAR} interpolation (shell environment merged over .env).</summary>
    public IReadOnlyDictionary<string, string> Env { get; init; } = new Dictionary<string, string>();

    public Func<string, bool> FileExists { get; init; } = File.Exists;

    /// <summary>Reports whether a stored secret exists (names only, never values); null skips the check.</summary>
    public Func<string, bool>? SecretExists { get; init; }
}
