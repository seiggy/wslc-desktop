using System.Text;

namespace WslcDesktop.Compose.Parsing;

/// <summary>Typed access to the untyped YAML tree plus command/shell-word handling.</summary>
public static partial class ComposeValues
{
    /// <summary>A command or entrypoint is either a list (used verbatim) or a string split like a shell would.</summary>
    internal static IReadOnlyList<string> Command(object? node) => node switch
    {
        string s => ShellSplit(s),
        List<object?> list => list.Select(x => x as string ?? string.Empty).ToList(),
        _ => [],
    };

    public static IReadOnlyList<string> ShellSplit(string text)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inToken = false;
        char quote = '\0';
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
                else if (c == '\\' && quote == '"' && i + 1 < text.Length && text[i + 1] is '"' or '\\')
                {
                    sb.Append(text[++i]);
                }
                else
                {
                    sb.Append(c);
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
                inToken = true;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (inToken)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                    inToken = false;
                }
            }
            else
            {
                sb.Append(c);
                inToken = true;
            }
        }

        if (inToken)
        {
            result.Add(sb.ToString());
        }

        return result;
    }

    internal static void AddFlag(List<string> args, string flag, object? value)
    {
        if (value is string s && s.Length > 0)
        {
            args.AddRange([flag, s]);
        }
    }

    internal static Dictionary<string, object?>? Map(object? o) => o as Dictionary<string, object?>;

    internal static List<object?>? List(object? o) => o as List<object?>;

    internal static IEnumerable<string> StringList(object? o) => o switch
    {
        string s => [s],
        List<object?> l => l.OfType<string>(),
        _ => [],
    };

    /// <summary>Reads a mapping or a list of "KEY=VALUE" / "KEY" entries; a null value means "take it from the environment".</summary>
    internal static IEnumerable<(string Key, string? Value)> KeyValues(object? o)
    {
        switch (o)
        {
            case Dictionary<string, object?> map:
                foreach (var (k, v) in map)
                {
                    yield return (k, v as string);
                }

                break;
            case List<object?> list:
                foreach (var item in list.OfType<string>())
                {
                    var eq = item.IndexOf('=');
                    yield return eq < 0 ? (item, null) : (item[..eq], item[(eq + 1)..]);
                }

                break;
        }
    }

    internal static bool IsTrue(object? o) => o is string s && s.Equals("true", StringComparison.OrdinalIgnoreCase);

    internal static bool IsFalse(object? o) => o is string s && s.Equals("false", StringComparison.OrdinalIgnoreCase);
}
