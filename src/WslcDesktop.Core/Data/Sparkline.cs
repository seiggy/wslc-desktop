using WslcDesktop.Containers;

namespace WslcDesktop.Data;

/// <summary>
/// Turns a run of samples into line points for a small chart: newest on the right edge, one fixed-width slot per
/// sample so the line grows in from the right until the window is full. Pure math, so it can be tested without UI.
/// </summary>
public static class Sparkline
{
    /// <summary>Samples shown in a list row: five minutes at the list's 5-second stats rate.</summary>
    public const int RowWindow = 60;

    /// <summary>CPU is drawn against at least this percentage, so an idle container isn't drawn as noisy spikes.</summary>
    public const double CpuFloorPercent = 10;

    public static IReadOnlyList<(double X, double Y)> Points(IReadOnlyList<double> values, double width, double height, double max, int slots)
    {
        if (values.Count < 2 || width <= 0 || height <= 0 || max <= 0 || slots < 2)
        {
            return [];
        }

        var start = Math.Max(0, values.Count - slots);
        var shown = values.Count - start;
        var step = width / (slots - 1);
        var points = new (double X, double Y)[shown];
        for (var i = 0; i < shown; i++)
        {
            var x = width - (shown - 1 - i) * step;
            var y = height - Math.Clamp(values[start + i] / max, 0, 1) * (height - 2) - 1;
            points[i] = (x, y);
        }

        return points;
    }

    /// <summary>CPU percent over the last <paramref name="slots"/> samples.</summary>
    public static IReadOnlyList<(double X, double Y)> Cpu(IReadOnlyList<StatsSample> history, double width, double height, int slots = RowWindow)
    {
        var values = Tail(history, slots).Select(s => s.CpuPercent).ToList();
        return Points(values, width, height, Math.Max(CpuFloorPercent, values.DefaultIfEmpty(0).Max()), slots);
    }

    /// <summary>
    /// Memory in use over the last <paramref name="slots"/> samples, from zero to a little above its peak: steady
    /// memory stays a flat line instead of magnifying a few kilobytes of noise.
    /// </summary>
    public static IReadOnlyList<(double X, double Y)> Memory(IReadOnlyList<StatsSample> history, double width, double height, int slots = RowWindow)
    {
        var values = Tail(history, slots).Select(s => (double)s.MemoryBytes).ToList();
        return Points(values, width, height, values.DefaultIfEmpty(0).Max() * 1.1, slots);
    }

    private static IEnumerable<StatsSample> Tail(IReadOnlyList<StatsSample> history, int slots) =>
        history.Skip(Math.Max(0, history.Count - slots));

    /// <summary>I/O is drawn against at least 1 KB/s, so a nearly idle container isn't drawn as spikes.</summary>
    public const double IoFloorBytesPerSecond = 1024;

    /// <summary>Per-second rates from cumulative counters (network or disk bytes); the first sample has no rate yet.</summary>
    public static List<double> Rates(IReadOnlyList<StatsSample> history, Func<StatsSample, ulong> pick)
    {
        var rates = new List<double>(history.Count);
        if (history.Count == 0)
        {
            return rates;
        }

        rates.Add(0);
        for (var i = 1; i < history.Count; i++)
        {
            var seconds = (history[i].Time - history[i - 1].Time).TotalSeconds;
            var (now, before) = (pick(history[i]), pick(history[i - 1]));
            // A counter that went backwards (container restarted) counts as no traffic, not a huge negative spike.
            rates.Add(seconds > 0 && now >= before ? (now - before) / seconds : 0);
        }

        return rates;
    }

    /// <summary>
    /// Two rates on one shared scale (receive/transmit, or read/write) over the last <paramref name="slots"/> samples,
    /// plus the latest value of each for a tooltip.
    /// </summary>
    public static (IReadOnlyList<(double X, double Y)> First, IReadOnlyList<(double X, double Y)> Second, double LatestFirst, double LatestSecond) Io(
        IReadOnlyList<StatsSample> history,
        Func<StatsSample, ulong> first,
        Func<StatsSample, ulong> second,
        double width,
        double height,
        int slots = RowWindow)
    {
        // One extra sample, so the oldest shown point has a real rate instead of the leading zero.
        var window = Tail(history, slots + 1).ToList();
        var a = Rates(window, first).Skip(window.Count > slots ? 1 : 0).ToList();
        var b = Rates(window, second).Skip(window.Count > slots ? 1 : 0).ToList();
        var max = Math.Max(IoFloorBytesPerSecond, Math.Max(a.DefaultIfEmpty(0).Max(), b.DefaultIfEmpty(0).Max()));
        return (Points(a, width, height, max, slots), Points(b, width, height, max, slots), a.LastOrDefault(), b.LastOrDefault());
    }
}
