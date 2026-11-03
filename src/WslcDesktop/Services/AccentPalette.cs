using System.Globalization;

namespace WslcDesktop.Services;

internal static class AccentPalette
{
    public static string? Normalize(string? value) =>
        value is { Length: 7 } && value[0] == '#'
        && uint.TryParse(value.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _)
            ? value.ToUpperInvariant()
            : null;

    public static uint Parse(string value) =>
        uint.Parse(value.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

    // WinUI uses dark shades on light surfaces and light shades on dark surfaces.
    // Keep custom accents readable even when the chosen color is almost white or black.
    public static uint[] Create(uint rgb)
    {
        var dark = ForContrast(rgb, 0, c => Contrast(c, 0xFFFFFF) >= 5.5);
        var light = ForContrast(rgb, 0xFFFFFF, c => Contrast(c, 0x202020) >= 5.5);
        return [rgb, dark, Mix(dark, 0, 0.15), Mix(dark, 0, 0.30),
            light, Mix(light, 0xFFFFFF, 0.15), Mix(light, 0xFFFFFF, 0.30)];
    }

    public static double Contrast(uint a, uint b)
    {
        var x = Luminance(a);
        var y = Luminance(b);
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    private static double Luminance(uint rgb)
    {
        static double Linear(uint channel)
        {
            var value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linear((rgb >> 16) & 255) + 0.7152 * Linear((rgb >> 8) & 255) + 0.0722 * Linear(rgb & 255);
    }

    private static uint ForContrast(uint rgb, uint target, Func<uint, bool> readable)
    {
        for (var step = 0; step <= 255; step++)
        {
            var candidate = Mix(rgb, target, step / 255.0);
            if (readable(candidate))
            {
                return candidate;
            }
        }

        return target;
    }

    private static uint Mix(uint rgb, uint target, double amount)
    {
        uint Channel(int shift) => (uint)Math.Round(((rgb >> shift) & 255) * (1 - amount) + ((target >> shift) & 255) * amount);
        return (Channel(16) << 16) | (Channel(8) << 8) | Channel(0);
    }
}
