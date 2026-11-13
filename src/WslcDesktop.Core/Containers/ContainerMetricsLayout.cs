namespace WslcDesktop.Containers;

/// <summary>How much of the container list's usage columns fits, widest first.</summary>
public enum MetricsTier
{
    /// <summary>Network and disk mini-charts, plus CPU and memory mini-charts and values.</summary>
    All,

    /// <summary>CPU and memory mini-charts and values.</summary>
    CpuMemoryCharts,

    /// <summary>CPU and memory values only.</summary>
    CpuMemoryValues,

    None,
}

/// <summary>
/// Sizes of the container list's usage cells and the list width each tier needs. As the window narrows, the network
/// and disk charts go first, then the CPU and memory charts, then the CPU and memory values.
/// </summary>
public static class ContainerMetricsLayout
{
    public const double ChartWidth = 56;
    public const double ChartHeight = 20;
    public const double CpuValueWidth = 48;
    public const double MemoryValueWidth = 72;
    public const double ChartValueGap = 8;
    public const double CellSpacing = 16;

    /// <summary>Everything else in a row: name (at a readable minimum), session, state, restart, actions, spacing and padding.</summary>
    public const double OtherColumns = 900;

    public static double CpuCellWidth(MetricsTier tier) => tier switch
    {
        MetricsTier.All or MetricsTier.CpuMemoryCharts => ChartWidth + ChartValueGap + CpuValueWidth,
        MetricsTier.CpuMemoryValues => CpuValueWidth,
        _ => 0,
    };

    public static double MemoryCellWidth(MetricsTier tier) => tier switch
    {
        MetricsTier.All or MetricsTier.CpuMemoryCharts => ChartWidth + ChartValueGap + MemoryValueWidth,
        MetricsTier.CpuMemoryValues => MemoryValueWidth,
        _ => 0,
    };

    /// <summary>Width of the usage cells together for a tier (the network and disk charts, then CPU, then memory).</summary>
    public static double Width(MetricsTier tier) => tier switch
    {
        MetricsTier.All => ChartWidth + CellSpacing + ChartWidth + CellSpacing + CpuCellWidth(tier) + CellSpacing + MemoryCellWidth(tier),
        MetricsTier.CpuMemoryCharts or MetricsTier.CpuMemoryValues => CpuCellWidth(tier) + CellSpacing + MemoryCellWidth(tier),
        _ => 0,
    };

    /// <summary>The widest tier that fits in a list this wide.</summary>
    public static MetricsTier For(double listWidth) =>
        new[] { MetricsTier.All, MetricsTier.CpuMemoryCharts, MetricsTier.CpuMemoryValues }
            .FirstOrDefault(t => listWidth >= OtherColumns + CellSpacing + Width(t), MetricsTier.None);
}
