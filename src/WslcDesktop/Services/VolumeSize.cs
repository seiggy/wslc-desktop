using System.Globalization;

namespace WslcDesktop.Services;

public enum SizeUnit
{
    MB,
    GB,
    TB,
}

/// <summary>Size rules for VHD volumes: wslc requires `--opt SizeBytes=N` with N between a VHDX minimum and 64 TB.</summary>
public static class VolumeSize
{
    public const long MinBytes = 3L * 1024 * 1024;

    public const long MaxBytes = 64L * 1024 * 1024 * 1024 * 1024;

    public const double DefaultGb = 10;

    public static readonly string[] UnitLabels = ["MB", "GB", "TB"];

    public static long UnitBytes(SizeUnit unit) => unit switch
    {
        SizeUnit.MB => 1L << 20,
        SizeUnit.TB => 1L << 40,
        _ => 1L << 30,
    };

    /// <summary>Converts a value and unit to bytes, or returns a friendly error.</summary>
    public static bool TryToBytes(double value, SizeUnit unit, out long bytes, out string error)
    {
        bytes = 0;
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
        {
            error = "Enter a size greater than zero.";
            return false;
        }

        var total = value * UnitBytes(unit);
        if (total < MinBytes)
        {
            error = "A VHD volume must be at least 3 MB.";
            return false;
        }

        if (total > MaxBytes)
        {
            error = "A VHD volume can be at most 64 TB.";
            return false;
        }

        bytes = (long)Math.Round(total);
        error = string.Empty;
        return true;
    }

    public static string[] CreateArguments(string session, string name, string driver, long? sizeBytes)
    {
        var args = new List<string> { "--session", session, "volume", "create", "--driver", driver };
        if (driver == "vhd" && sizeBytes is { } size)
        {
            args.AddRange(["--opt", $"SizeBytes={size.ToString(CultureInfo.InvariantCulture)}"]);
        }

        args.Add(name);
        return [.. args];
    }
}
