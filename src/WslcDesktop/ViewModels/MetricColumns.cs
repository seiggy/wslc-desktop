using System.ComponentModel;
using Microsoft.UI.Xaml;
using WslcDesktop.Containers;

namespace WslcDesktop.ViewModels;

/// <summary>
/// Which usage cells the container list shows at its current width, shared by the header and every row so they
/// collapse together and stay aligned.
/// </summary>
public sealed class MetricColumns : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public MetricsTier Tier { get; private set; } = MetricsTier.All;

    /// <summary>Picks the widest tier that fits; returns true when it changed.</summary>
    public bool Fit(double listWidth)
    {
        var tier = ContainerMetricsLayout.For(listWidth);
        if (tier == Tier)
        {
            return false;
        }

        Tier = tier;
        foreach (var property in new[] { nameof(Tier), nameof(IoVisibility), nameof(ChartVisibility), nameof(ValueVisibility), nameof(CpuWidth), nameof(MemoryWidth) })
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }

        return true;
    }

    public bool ShowsIo => Tier == MetricsTier.All;

    public bool ShowsCharts => Tier is MetricsTier.All or MetricsTier.CpuMemoryCharts;

    public Visibility IoVisibility => ShowsIo ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ChartVisibility => ShowsCharts ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ValueVisibility => Tier != MetricsTier.None ? Visibility.Visible : Visibility.Collapsed;

    public double CpuWidth => ContainerMetricsLayout.CpuCellWidth(Tier);

    public double MemoryWidth => ContainerMetricsLayout.MemoryCellWidth(Tier);
}
