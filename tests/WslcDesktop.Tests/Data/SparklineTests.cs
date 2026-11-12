using WslcDesktop.Containers;
using WslcDesktop.Data;

namespace WslcDesktop.Tests.Data;

public class SparklineTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    private static StatsSample At(int seconds, double cpu = 0, ulong memory = 0, ulong rx = 0, ulong tx = 0) =>
        new(T0.AddSeconds(seconds), cpu, 0, memory, 0, rx, tx, 0, 0, 1);

    [Fact]
    public void The_newest_point_is_on_the_right_edge_and_the_line_grows_in_from_the_right()
    {
        var points = Sparkline.Points([0, 50, 100], width: 100, height: 22, max: 100, slots: 11);

        Assert.Equal([80.0, 90.0, 100.0], points.Select(p => p.X));
        Assert.Equal(21.0, points[0].Y);
        Assert.Equal(1.0, points[2].Y);
    }

    [Fact]
    public void Only_the_last_window_of_samples_is_drawn()
    {
        var points = Sparkline.Points(Enumerable.Range(0, 100).Select(i => (double)i).ToList(), 100, 20, 100, slots: 10);

        Assert.Equal(10, points.Count);
        Assert.Equal(0.0, points[0].X);
    }

    [Fact]
    public void A_single_sample_or_an_empty_scale_draws_nothing()
    {
        Assert.Empty(Sparkline.Points([5], 56, 20, 10, 60));
        Assert.Empty(Sparkline.Points([0, 0], 56, 20, 0, 60));
    }

    [Fact]
    public void An_idle_cpu_is_drawn_against_the_floor_not_magnified()
    {
        var points = Sparkline.Cpu([At(0, cpu: 0.1), At(5, cpu: 0.2)], 56, 20);

        // 0.2% of a 10% scale: barely above the baseline.
        Assert.All(points, p => Assert.True(p.Y > 18));
    }

    [Fact]
    public void Steady_memory_is_a_flat_line()
    {
        var points = Sparkline.Memory([At(0, memory: 40_000_000), At(5, memory: 40_000_100), At(10, memory: 40_000_000)], 56, 20);

        Assert.True(points.Max(p => p.Y) - points.Min(p => p.Y) < 0.01);
    }

    [Fact]
    public void Rates_come_from_counter_deltas_and_a_reset_counts_as_no_traffic()
    {
        var rates = Sparkline.Rates([At(0, rx: 0), At(5, rx: 5000), At(10, rx: 100)], s => s.NetRxBytes);

        Assert.Equal([0.0, 1000.0, 0.0], rates);
    }

    [Fact]
    public void Io_shares_one_scale_and_reports_the_latest_rates()
    {
        var history = new[] { At(0), At(5, rx: 10_000, tx: 5_000), At(10, rx: 20_000, tx: 5_000) };

        var io = Sparkline.Io(history, s => s.NetRxBytes, s => s.NetTxBytes, 56, 20);

        Assert.Equal(2000.0, io.LatestFirst);
        Assert.Equal(0.0, io.LatestSecond);
        Assert.Equal(1.0, io.First[^1].Y);
        Assert.Equal(19.0, io.Second[^1].Y);
    }

    [Fact]
    public void A_full_io_window_drops_the_leading_zero_rate()
    {
        // A steady 10 KB/s, well above the 1 KB/s floor, so every shown point sits at the top.
        var history = Enumerable.Range(0, 5).Select(i => At(i * 5, rx: (ulong)(i * 50_000))).ToList();

        var io = Sparkline.Io(history, s => s.NetRxBytes, s => s.NetTxBytes, 56, 20, slots: 4);

        Assert.Equal(4, io.First.Count);
        Assert.All(io.First, p => Assert.Equal(1.0, p.Y));
    }
}

public class ContainerMetricsLayoutTests
{
    [Fact]
    public void Narrowing_drops_io_charts_then_cpu_and_memory_charts_then_their_values()
    {
        var all = ContainerMetricsLayout.OtherColumns + ContainerMetricsLayout.CellSpacing + ContainerMetricsLayout.Width(MetricsTier.All);
        var charts = ContainerMetricsLayout.OtherColumns + ContainerMetricsLayout.CellSpacing + ContainerMetricsLayout.Width(MetricsTier.CpuMemoryCharts);
        var values = ContainerMetricsLayout.OtherColumns + ContainerMetricsLayout.CellSpacing + ContainerMetricsLayout.Width(MetricsTier.CpuMemoryValues);

        Assert.True(all > charts && charts > values);
        Assert.Equal(MetricsTier.All, ContainerMetricsLayout.For(all + 200));
        Assert.Equal(MetricsTier.All, ContainerMetricsLayout.For(all));
        Assert.Equal(MetricsTier.CpuMemoryCharts, ContainerMetricsLayout.For(all - 1));
        Assert.Equal(MetricsTier.CpuMemoryValues, ContainerMetricsLayout.For(charts - 1));
        Assert.Equal(MetricsTier.None, ContainerMetricsLayout.For(values - 1));
    }

    [Fact]
    public void Cells_are_sized_for_what_they_show()
    {
        Assert.Equal(ContainerMetricsLayout.ChartWidth + ContainerMetricsLayout.ChartValueGap + ContainerMetricsLayout.CpuValueWidth, ContainerMetricsLayout.CpuCellWidth(MetricsTier.All));
        Assert.Equal(ContainerMetricsLayout.CpuValueWidth, ContainerMetricsLayout.CpuCellWidth(MetricsTier.CpuMemoryValues));
        Assert.Equal(0, ContainerMetricsLayout.MemoryCellWidth(MetricsTier.None));
    }
}
