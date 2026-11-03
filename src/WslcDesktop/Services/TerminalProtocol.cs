using System.Text;
using System.Text.Json;

namespace WslcDesktop.Services;

/// <summary>UI-free helpers for the terminal: command lines, the WebView message protocol and colour schemes.</summary>
public static class TerminalProtocol
{
    public const short DefaultColumns = 120;
    public const short DefaultRows = 30;

    public static readonly string[] Fonts = ["Cascadia Mono", "Cascadia Code", "Consolas", "Courier New"];
    public static readonly int[] FontSizes = [11, 12, 13, 14, 16, 18, 20];

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

    public static string OutputMessage(ReadOnlySpan<byte> data) =>
        JsonSerializer.Serialize(new Dictionary<string, string> { ["t"] = "out", ["d"] = Convert.ToBase64String(data) });

    public static string NoticeMessage(string text) =>
        JsonSerializer.Serialize(new Dictionary<string, string> { ["t"] = "notice", ["d"] = text });

    public static string CommandMessage(string type) =>
        JsonSerializer.Serialize(new Dictionary<string, string> { ["t"] = type });

    public static string StyleMessage(bool dark, string fontFamily, int fontSize) =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["t"] = "style",
            ["theme"] = Palette(dark),
            ["fontFamily"] = $"\"{fontFamily}\", Consolas, monospace",
            ["fontSize"] = fontSize,
        });

    /// <summary>Parses a message from the page; returns null for anything malformed.</summary>
    public static TerminalMessage? ParseMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("t", out var t))
            {
                return null;
            }

            switch (t.GetString())
            {
                case "in" when root.TryGetProperty("d", out var d) && d.ValueKind == JsonValueKind.String:
                    return new TerminalMessage(TerminalMessageKind.Input, Data: d.GetString());
                case "bin" when root.TryGetProperty("d", out var b) && b.ValueKind == JsonValueKind.String:
                    return new TerminalMessage(TerminalMessageKind.Binary, Data: b.GetString());
                case "size" or "ready":
                    var cols = root.TryGetProperty("cols", out var c) && c.TryGetInt32(out var ci) ? ci : 0;
                    var rows = root.TryGetProperty("rows", out var r) && r.TryGetInt32(out var ri) ? ri : 0;
                    return new TerminalMessage(
                        t.GetString() == "ready" ? TerminalMessageKind.Ready : TerminalMessageKind.Size,
                        Columns: ClampDimension(cols),
                        Rows: ClampDimension(rows));
                default:
                    return null;
            }
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Typed text is UTF-8 encoded.</summary>
    public static byte[] EncodeInput(string data) => Encoding.UTF8.GetBytes(data);

    /// <summary>xterm reports binary (non-UTF-8) mouse input as a string whose chars are bytes 0-255.</summary>
    public static byte[] EncodeBinaryInput(string data) => Encoding.Latin1.GetBytes(data);

    public static short ClampDimension(int value) => (short)Math.Clamp(value, 0, 1000);

    /// <summary>Windows Terminal's Campbell (dark) and One Half Light-style (light) schemes, on a transparent background.</summary>
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

public enum TerminalMessageKind
{
    Ready,
    Size,
    Input,
    Binary,
}

public sealed record TerminalMessage(TerminalMessageKind Kind, string? Data = null, short Columns = 0, short Rows = 0);
