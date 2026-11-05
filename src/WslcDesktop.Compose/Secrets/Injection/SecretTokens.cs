using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using WslcDesktop.Compose.Secrets;

namespace WslcDesktop.Compose.Secrets.Injection;

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
