using System.Text;

namespace WslcDesktop.Terminal;

/// <summary>UI-free helpers for the terminal: command lines, and colour schemes.</summary>
public static class TerminalProtocol
{
    public const short DefaultColumns = 120;
    public const short DefaultRows = 30;

    public const string DefaultFont = "Cascadia Mono";
    public const int DefaultFontSize = 14;
    public const int MinFontSize = 8;
    public const int MaxFontSize = 32;

    /// <summary>Well-known monospace families offered when they're installed.</summary>
    public static readonly string[] CandidateFonts =
    [
        "Cascadia Mono", "Cascadia Code", "Consolas", "Courier New", "Lucida Console", "JetBrains Mono", "Fira Code",
        "Source Code Pro", "Hack", "Iosevka", "MesloLGS NF", "Roboto Mono", "Ubuntu Mono", "Inconsolata", "Cousine",
    ];

    public static int ClampFontSize(int size) => Math.Clamp(size, MinFontSize, MaxFontSize);

    public static string NormalizeFont(string? family) => string.IsNullOrWhiteSpace(family) ? DefaultFont : family.Trim();

    /// <summary>The candidate fonts present in <paramref name="installed"/> (registry-style names such as "Consolas (TrueType)"), always including <paramref name="current"/> and falling back to Consolas.</summary>
    public static List<string> AvailableFonts(IEnumerable<string> installed, string current)
    {
        var names = installed.ToList();
        bool Has(string family) => names.Any(n => n.StartsWith(family, StringComparison.OrdinalIgnoreCase)
            && (n.Length == family.Length || n[family.Length] is ' ' or '('));
        var result = CandidateFonts.Where(Has).ToList();
        if (result.Count == 0)
        {
            result.Add("Consolas");
        }

        if (!result.Contains(current, StringComparer.OrdinalIgnoreCase))
        {
            result.Insert(0, current);
        }

        return result;
    }

    /// <summary>Quotes one argument per the Windows <c>CommandLineToArgvW</c> rules.</summary>
    public static string QuoteArgument(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
        {
            return arg;
        }

        var sb = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                sb.Append('\\', backslashes * 2 + 1).Append('"');
            }
            else
            {
                sb.Append('\\', backslashes).Append(c);
            }

            backslashes = 0;
        }

        return sb.Append('\\', backslashes * 2).Append('"').ToString();
    }

    public static string BuildCommandLine(string executable, IEnumerable<string> args) =>
        string.Join(' ', new[] { QuoteArgument(executable) }.Concat(args.Select(QuoteArgument)));

    /// <summary>The <c>wslc exec</c> argument list for a TTY shell, advertising a colour-capable terminal.</summary>
    public static string[] ExecShellArguments(string session, string container, string shell) =>
        ["--session", session, "exec", "--interactive", "--tty", "-e", "TERM=xterm-256color", "-e", "COLORTERM=truecolor", container, shell];

    /// <summary>Typed text is UTF-8 encoded.</summary>
    public static byte[] EncodeInput(string data) => Encoding.UTF8.GetBytes(data);

    public static short ClampDimension(int value) => (short)Math.Clamp(value, 0, 1000);

    /// <summary>Windows Terminal's Campbell (dark) and One Half Light-style (light) schemes.</summary>
    public static Dictionary<string, string> Palette(bool dark) => dark
        ? new()
        {
            ["background"] = "#0C0C0C", ["foreground"] = "#CCCCCC", ["cursor"] = "#FFFFFF", ["cursorAccent"] = "#0C0C0C",
            ["selectionBackground"] = "#264F78",
            ["black"] = "#0C0C0C", ["red"] = "#C50F1F", ["green"] = "#13A10E", ["yellow"] = "#C19C00",
            ["blue"] = "#0037DA", ["magenta"] = "#881798", ["cyan"] = "#3A96DD", ["white"] = "#CCCCCC",
            ["brightBlack"] = "#767676", ["brightRed"] = "#E74856", ["brightGreen"] = "#16C60C", ["brightYellow"] = "#F9F1A5",
            ["brightBlue"] = "#3B78FF", ["brightMagenta"] = "#B4009E", ["brightCyan"] = "#61D6D6", ["brightWhite"] = "#F2F2F2",
        }
        : new()
        {
            ["background"] = "#FAFAFA", ["foreground"] = "#383A42", ["cursor"] = "#4F525D", ["cursorAccent"] = "#FAFAFA",
            ["selectionBackground"] = "#BFCEFF",
            ["black"] = "#383A42", ["red"] = "#E45649", ["green"] = "#50A14F", ["yellow"] = "#C18301",
            ["blue"] = "#0184BC", ["magenta"] = "#A626A4", ["cyan"] = "#0997B3", ["white"] = "#FAFAFA",
            ["brightBlack"] = "#4F525D", ["brightRed"] = "#DF6C75", ["brightGreen"] = "#98C379", ["brightYellow"] = "#E4C07A",
            ["brightBlue"] = "#4FA6ED", ["brightMagenta"] = "#C577DD", ["brightCyan"] = "#47B8BF", ["brightWhite"] = "#FFFFFF",
        };
}
