using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WslcDesktop.Compose.Secrets.Injection;

public static partial class SecretInjection
{
    // Names that would change how wslc itself runs if overridden in its environment; these go through an env file instead.
    private static readonly HashSet<string> ProcessCritical = new(StringComparer.OrdinalIgnoreCase)
    {
        "PATH", "PATHEXT", "SYSTEMROOT", "SYSTEMDRIVE", "WINDIR", "COMSPEC", "TEMP", "TMP", "USERPROFILE", "APPDATA",
        "LOCALAPPDATA", "PROGRAMDATA", "PROGRAMFILES", "HOMEDRIVE", "HOMEPATH", "USERNAME", "COMPUTERNAME", "NUMBER_OF_PROCESSORS",
    };

    /// <summary>
    /// Resolves <c>--env KEY=...${secret:x}...</c> arguments. Normally the value is handed over as an inherited environment
    /// variable (<c>--env KEY</c>), so it never appears in a process command line. Names that can't be safely set on the
    /// wslc process use a temporary <c>--env-file</c> that is deleted when the command is disposed.
    /// </summary>
    public static PreparedCommand Prepare(IReadOnlyList<string> args, Func<string, string?> lookup)
    {
        var result = new List<string>(args.Count);
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var fileLines = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg is "--env" or "-e" && i + 1 < args.Count && SecretTokens.Contains(args[i + 1]))
            {
                var entry = args[++i];
                var eq = entry.IndexOf('=');
                if (eq <= 0)
                {
                    throw new InvalidOperationException("A secret can only be used as the value of KEY=VALUE.");
                }

                var key = entry[..eq];
                var value = SecretTokens.Resolve(entry[(eq + 1)..], lookup);
                if (!EnvName().IsMatch(key))
                {
                    throw new InvalidOperationException($"'{key}' isn't a valid environment variable name.");
                }

                if (ProcessCritical.Contains(key) || key.StartsWith("WSL", StringComparison.OrdinalIgnoreCase) || env.ContainsKey(key))
                {
                    if (value.Contains('\n') || value.Contains('\r'))
                    {
                        throw new InvalidOperationException($"The value for {key} spans several lines, which isn't supported for this variable name.");
                    }

                    fileLines.Add($"{key}={value}");
                }
                else
                {
                    env[key] = value;
                    result.AddRange(["--env", key]);
                }
            }
            else if (SecretTokens.Contains(arg))
            {
                throw new InvalidOperationException("Secrets can only be used in environment variable values.");
            }
            else
            {
                result.Add(arg);
            }
        }

        var temp = new List<string>();
        if (fileLines.Count > 0)
        {
            var path = Path.Combine(Path.GetTempPath(), "wslcdesktop-" + Guid.NewGuid().ToString("N") + ".env");
            File.WriteAllLines(path, fileLines, new UTF8Encoding(false));
            temp.Add(path);
            // Insert before the image so it stays a run/create option.
            var firstEnvish = result.FindIndex(a => a is "run" or "create");
            result.InsertRange(firstEnvish + 1, ["--env-file", path]);
        }

        return new PreparedCommand(result, env, temp);
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex EnvName();
}
