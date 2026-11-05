using System.Globalization;
using System.Text.RegularExpressions;

namespace WslcDesktop.Compose.Parsing;

/// <summary>Converts compose memory and duration strings to the forms wslc expects.</summary>
public static partial class ComposeUnits
{
    internal static string Quote(string arg) =>
        arg.Length > 0 && arg.IndexOfAny([' ', '"', '\'', '\t']) < 0 ? arg : "\"" + arg.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    internal static string NormalizeMemory(string mem)
    {
        var m = MemoryRegex().Match(mem.Trim());
        if (!m.Success)
        {
            return mem;
        }

        var unit = m.Groups[2].Value.ToUpperInvariant().TrimEnd('B');
        return m.Groups[1].Value + unit;
    }

    public static int? ParseDurationSeconds(string text)
    {
        var total = 0.0;
        var matches = DurationRegex().Matches(text.Trim());
        if (matches.Count == 0 || string.Concat(matches.Select(m => m.Value)) != text.Trim())
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var plain) ? (int)Math.Ceiling(plain) : null;
        }

        foreach (Match m in matches)
        {
            var n = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            total += m.Groups[2].Value switch
            {
                "h" => n * 3600,
                "m" => n * 60,
                "s" => n,
                "ms" => n / 1000,
                _ => 0,
            };
        }

        return (int)Math.Ceiling(total);
    }


    [GeneratedRegex(@"^(\d+(?:\.\d+)?)\s*([a-zA-Z]*)$")]
    internal static partial Regex MemoryRegex();
    [GeneratedRegex(@"(\d+(?:\.\d+)?)(ms|h|m|s)")]
    internal static partial Regex DurationRegex();
}
