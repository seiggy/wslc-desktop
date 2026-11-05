using WslcDesktop.Localization;
using System.Text;
using WslcDesktop.Compose.Secrets.Injection;

namespace WslcDesktop.Compose.Parsing;

/// <summary>Compose variable interpolation: ${VAR}, ${VAR:-d}, ${VAR-d}, ${VAR:?e}, ${VAR?e}, ${VAR:+r}, ${VAR+r}, $VAR and $$.</summary>
public static class Interpolator
{
    public static string Apply(string input, IReadOnlyDictionary<string, string> env)
    {
        if (input.IndexOf('$') < 0)
        {
            return input;
        }

        var sb = new StringBuilder();
        var i = 0;
        while (i < input.Length)
        {
            var c = input[i];
            if (c != '$')
            {
                sb.Append(c);
                i++;
                continue;
            }

            if (i + 1 >= input.Length)
            {
                sb.Append('$');
                i++;
                continue;
            }

            var next = input[i + 1];
            if (next == '$')
            {
                sb.Append('$');
                i += 2;
            }
            else if (next == '{')
            {
                var end = FindClose(input, i + 2);
                if (end < 0)
                {
                    throw new ComposeException(UiStrings.Format("InvalidInterpolationMissingIn", input));
                }

                var body = input[(i + 2)..end];
                if (body.StartsWith(SecretTokens.Prefix, StringComparison.Ordinal))
                {
                    // Secret tokens are resolved at run time, never during planning.
                    var secretName = body[SecretTokens.Prefix.Length..];
                    sb.Append(SecretTokens.IsValidName(secretName)
                        ? SecretTokens.Format(secretName)
                        : throw new ComposeException(UiStrings.Format("InvalidSecretReferenceNamesUseLettersDigitsAnd", body)));
                }
                else
                {
                    sb.Append(Braced(body, env));
                }

                i = end + 1;
            }
            else if (IsNameStart(next))
            {
                var j = i + 1;
                while (j < input.Length && IsNameChar(input[j]))
                {
                    j++;
                }

                sb.Append(env.GetValueOrDefault(input[(i + 1)..j]) ?? string.Empty);
                i = j;
            }
            else
            {
                throw new ComposeException(UiStrings.Format("InvalidInterpolationFormatInUseForALiteralDollar", input));
            }
        }

        return sb.ToString();
    }

    private static int FindClose(string s, int from)
    {
        var depth = 1;
        for (var i = from; i < s.Length; i++)
        {
            if (s[i] == '{')
            {
                depth++;
            }
            else if (s[i] == '}' && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private static string Braced(string body, IReadOnlyDictionary<string, string> env)
    {
        var n = 0;
        while (n < body.Length && IsNameChar(body[n]))
        {
            n++;
        }

        var name = body[..n];
        if (name.Length == 0 || !IsNameStart(name[0]))
        {
            throw new ComposeException(UiStrings.Format("InvalidInterpolationVariableIn", body));
        }

        var rest = body[n..];
        env.TryGetValue(name, out var value);
        var isSet = value is not null;
        var isNonEmpty = !string.IsNullOrEmpty(value);

        if (rest.Length == 0)
        {
            return value ?? string.Empty;
        }

        string Operand(int skip) => Apply(rest[skip..], env);

        if (rest.StartsWith(":-", StringComparison.Ordinal))
        {
            return isNonEmpty ? value! : Operand(2);
        }

        if (rest.StartsWith('-'))
        {
            return isSet ? value! : Operand(1);
        }

        if (rest.StartsWith(":?", StringComparison.Ordinal))
        {
            return isNonEmpty ? value! : throw new ComposeException(UiStrings.Format("RequiredVariableIsMissing", name, Operand(2)));
        }

        if (rest.StartsWith('?'))
        {
            return isSet ? value! : throw new ComposeException(UiStrings.Format("RequiredVariableIsMissing", name, Operand(1)));
        }

        if (rest.StartsWith(":+", StringComparison.Ordinal))
        {
            return isNonEmpty ? Operand(2) : string.Empty;
        }

        if (rest.StartsWith('+'))
        {
            return isSet ? Operand(1) : string.Empty;
        }

        throw new ComposeException(UiStrings.Format("InvalidInterpolationFormatFor", body));
    }

    private static bool IsNameStart(char c) => char.IsAsciiLetter(c) || c == '_';

    private static bool IsNameChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';

    /// <summary>Walks a parsed YAML tree and interpolates every string value in place.</summary>
    public static object? ApplyTree(object? node, IReadOnlyDictionary<string, string> env) => node switch
    {
        string s => Apply(s, env),
        List<object?> list => list.Select(x => ApplyTree(x, env)).ToList(),
        Dictionary<string, object?> map => map.ToDictionary(kv => kv.Key, kv => ApplyTree(kv.Value, env), StringComparer.Ordinal),
        _ => node,
    };

    /// <summary>Parses KEY=VALUE lines (as in a .env file), ignoring comments and blanks and stripping matching quotes.</summary>
    public static Dictionary<string, string> ParseEnvFile(string content)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line[7..].TrimStart();
            }

            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
            {
                value = value[1..^1];
            }
            else
            {
                var hash = value.IndexOf(" #", StringComparison.Ordinal);
                if (hash >= 0)
                {
                    value = value[..hash].TrimEnd();
                }
            }

            result[key] = value;
        }

        return result;
    }
}
