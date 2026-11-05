using System.Globalization;
using System.Text.RegularExpressions;

namespace WslcDesktop.Restart.Monitoring;

/// <summary>Pulls the exit code out of a `wslc list` status such as "Exited (137) 4 seconds ago".</summary>
public static partial class ContainerStatusText
{
    public static int? ExitCode(string? status)
    {
        var m = ExitRegex().Match(status ?? string.Empty);
        return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code)
            ? code
            : null;
    }

    [GeneratedRegex(@"\((-?\d+)\)")]
    private static partial Regex ExitRegex();
}
