namespace WslcDesktop.Compose.Planning;

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
