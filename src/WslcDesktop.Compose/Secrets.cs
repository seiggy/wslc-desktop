using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WslcDesktop.Compose;

/// <summary>Raised when a token references a secret that isn't in the store.</summary>
public sealed class SecretUnavailableException(string name)
    : InvalidOperationException($"The secret '{name}' isn't stored. Add it on the Secrets page.")
{
    public string SecretName { get; } = name;
}

/// <summary>The <c>${secret:NAME}</c> token syntax used to reference a stored secret without ever writing its value down.</summary>
public static partial class SecretTokens
{
    public const string Prefix = "secret:";

    public const int MaxNameLength = 64;

    public static string Format(string name) => "${" + Prefix + name + "}";

    public static bool IsValidName(string name) => name.Length is > 0 and <= MaxNameLength && NameRegex().IsMatch(name);

    public static bool Contains(string text) => text.Contains("${" + Prefix, StringComparison.Ordinal) && TokenRegex().IsMatch(text);

    public static bool ContainsAny(IEnumerable<string> values) => values.Any(Contains);

    /// <summary>Names referenced by tokens in <paramref name="text"/>, in order of appearance (distinct).</summary>
    public static IEnumerable<string> Find(string text) =>
        text.Contains("${" + Prefix, StringComparison.Ordinal)
            ? TokenRegex().Matches(text).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal)
            : [];

    /// <summary>Replaces every token with its value; throws <see cref="SecretUnavailableException"/> when one is missing.</summary>
    public static string Resolve(string text, Func<string, string?> lookup) =>
        Contains(text)
            ? TokenRegex().Replace(text, m => lookup(m.Groups[1].Value) ?? throw new SecretUnavailableException(m.Groups[1].Value))
            : text;

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.\-]*$")]
    private static partial Regex NameRegex();

    [GeneratedRegex(@"\$\{secret:([A-Za-z0-9][A-Za-z0-9_.\-]{0,63})\}")]
    private static partial Regex TokenRegex();
}

/// <summary>Generates random values for password-like secrets.</summary>
public static class SecretGenerator
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    public static string Create(int length = 32)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 8);
        return System.Security.Cryptography.RandomNumberGenerator.GetString(Alphabet, length);
    }
}

/// <summary>
/// Replaces known secret values in text with their <c>${secret:NAME}</c> token. This is substring matching, so it is
/// best-effort: a value that was transformed (split, encoded, truncated) or is shorter than <see cref="MinLength"/> is not caught.
/// </summary>
public sealed class SecretRedactor
{
    /// <summary>Values shorter than this are not redacted; replacing a couple of characters would corrupt unrelated text.</summary>
    public const int MinLength = 4;

    public static readonly SecretRedactor Empty = new([]);

    private readonly (string Needle, string Token)[] _rules;

    public SecretRedactor(IEnumerable<KeyValuePair<string, string>> secrets)
    {
        var rules = new List<(string, string)>();
        foreach (var (name, value) in secrets)
        {
            if (value.Length < MinLength)
            {
                continue;
            }

            var token = SecretTokens.Format(name);
            // Raw JSON escapes some characters; match both the plain and the escaped spellings.
            foreach (var form in new[]
            {
                value,
                JsonEncodedText.Encode(value).ToString(),
                JsonEncodedText.Encode(value, JavaScriptEncoder.UnsafeRelaxedJsonEscaping).ToString(),
            }.Distinct(StringComparer.Ordinal))
            {
                rules.Add((form, token));
            }
        }

        _rules = [.. rules.OrderByDescending(r => r.Item1.Length)];
    }

    public bool IsEmpty => _rules.Length == 0;

    public string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text) || _rules.Length == 0)
        {
            return text ?? string.Empty;
        }

        var result = text;
        foreach (var (needle, token) in _rules)
        {
            result = result.Replace(needle, token, StringComparison.Ordinal);
        }

        return result;
    }
}

/// <summary>A wslc invocation whose secret values travel in the child's environment (or a short-lived env file), not on the command line.</summary>
public sealed class PreparedCommand(IReadOnlyList<string> args, IReadOnlyDictionary<string, string> environment, IReadOnlyList<string> tempFiles) : IDisposable
{
    public IReadOnlyList<string> Args { get; } = args;

    /// <summary>Extra variables for the wslc process; <c>--env NAME</c> makes wslc pass the inherited value to the container.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; } = environment;

    public void Dispose()
    {
        foreach (var file in tempFiles)
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

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
