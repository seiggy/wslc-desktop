using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WslcDesktop.Compose.Secrets.Injection;

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
