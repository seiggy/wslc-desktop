using System.Text;

namespace WslcDesktop.Terminal;

/// <summary>Encodes keyboard and mouse input as the xterm sequences a terminal application expects.</summary>
public static class VtInput
{
    /// <summary>The sequence for a non-text key (virtual-key code), or null when the key produces text through character input.</summary>
    public static string? EncodeKey(int vk, bool ctrl, bool shift, bool alt, bool appCursor)
    {
        var mod = 1 + (shift ? 1 : 0) + (alt ? 2 : 0) + (ctrl ? 4 : 0);

        string Csi(char final) => mod > 1 ? $"\u001b[1;{mod}{final}" : appCursor ? $"\u001bO{final}" : $"\u001b[{final}";
        string Tilde(int n) => mod > 1 ? $"\u001b[{n};{mod}~" : $"\u001b[{n}~";
        string Ss3(char final) => mod > 1 ? $"\u001b[1;{mod}{final}" : $"\u001bO{final}";

        var result = vk switch
        {
            8 => ctrl ? "\u0008" : "\u007f",
            9 => shift ? "\u001b[Z" : "\t",
            13 => "\r",
            27 => "\u001b",
            33 => Tilde(5),
            34 => Tilde(6),
            35 => Csi('F'),
            36 => Csi('H'),
            37 => Csi('D'),
            38 => Csi('A'),
            39 => Csi('C'),
            40 => Csi('B'),
            45 => Tilde(2),
            46 => Tilde(3),
            >= 112 and <= 115 => Ss3((char)('P' + vk - 112)),
            116 => Tilde(15),
            117 => Tilde(17),
            118 => Tilde(18),
            119 => Tilde(19),
            120 => Tilde(20),
            121 => Tilde(21),
            122 => Tilde(23),
            123 => Tilde(24),
            _ => null,
        };

        if (result is not null && alt && vk is 8 or 9 or 13 or 27)
        {
            return "\u001b" + result;
        }

        if (result is null && ctrl && !shift)
        {
            return vk switch
            {
                >= 65 and <= 90 => ((char)(vk - 64)).ToString(),
                32 => "\0",
                219 => "\u001b",
                220 => "\u001c",
                221 => "\u001d",
                _ => null,
            };
        }

        return result;
    }

    /// <summary>Wraps pasted text for the application: newlines become carriage returns and bracketed paste is honoured.</summary>
    public static string EncodePaste(string text, bool bracketed)
    {
        var normalized = text.Replace("\r\n", "\r").Replace('\n', '\r');
        return bracketed ? "\u001b[200~" + normalized + "\u001b[201~" : normalized;
    }

    /// <summary>An xterm mouse report. <paramref name="button"/>: 0 left, 1 middle, 2 right, 64/65 wheel up/down, 3 = release (legacy).</summary>
    public static string EncodeMouse(int button, int column, int row, bool release, bool motion, bool sgr, bool shift = false, bool alt = false, bool ctrl = false)
    {
        var code = button + (shift ? 4 : 0) + (alt ? 8 : 0) + (ctrl ? 16 : 0) + (motion ? 32 : 0);
        if (sgr)
        {
            return $"\u001b[<{code};{column + 1};{row + 1}{(release ? 'm' : 'M')}";
        }

        var sb = new StringBuilder("\u001b[M");
        sb.Append((char)(32 + (release ? 3 + (code & ~3) : code)));
        sb.Append((char)Math.Min(255, 32 + column + 1));
        sb.Append((char)Math.Min(255, 32 + row + 1));
        return sb.ToString();
    }
}
