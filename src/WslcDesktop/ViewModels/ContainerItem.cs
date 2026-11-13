using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WslcDesktop.Compose;
using WslcDesktop.Compose.Execution;
using WslcDesktop.Containers;
using WslcDesktop.Data;
using WslcDesktop.Localization;
using WslcDesktop.Restart;
using WslcDesktop.Services;

namespace WslcDesktop.ViewModels;

/// <summary>A container row that updates in place as state and telemetry change.</summary>
public sealed class ContainerItem(string session, bool isThisApp, string name) : INotifyPropertyChanged
{
    private string _image = string.Empty;
    private string _state = string.Empty;
    private string _cpu = "–";
    private string _memory = "–";
    private string _stack = string.Empty;
    private string _service = string.Empty;
    private string _header = string.Empty;
    private string _restart = string.Empty;
    private bool _restartAlert;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; } = name;

    public string Session { get; } = session;

    public bool IsThisApp { get; } = isThisApp;

    public string Key => $"{Session}|{Name}";

    public Visibility ThisAppVisibility => IsThisApp ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The compose project this container belongs to, or empty for a standalone container.</summary>
    public string Stack { get => _stack; private set => Set(ref _stack, value); }

    public string Service { get => _service; private set { Set(ref _service, value); } }

    /// <summary>Group heading shown above the first container of a stack; empty elsewhere.</summary>
    public string Header { get => _header; private set => Set(ref _header, value); }

    public Visibility HeaderVisibility => _header.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public string ImageText => _service.Length > 0 ? $"{_service} · {_image}" : _image;

    public void SetHeader(string header) => Header = header;

    public string Image { get => _image; private set => Set(ref _image, value); }

    public string State { get => _state; private set => Set(ref _state, value); }

    public string Cpu { get => _cpu; private set => Set(ref _cpu, value); }

    public string Memory { get => _memory; private set => Set(ref _memory, value); }

    /// <summary>Which usage cells the list shows; shared with the header so every row collapses the same way.</summary>
    public MetricColumns Columns { get; init; } = new();

    private PointCollection _cpuPoints = [];
    private PointCollection _memoryPoints = [];
    private PointCollection _netRxPoints = [];
    private PointCollection _netTxPoints = [];
    private PointCollection _diskReadPoints = [];
    private PointCollection _diskWritePoints = [];
    private string _netText = string.Empty;
    private string _diskText = string.Empty;

    /// <summary>CPU over the last few minutes, for the row's mini-chart.</summary>
    public PointCollection CpuPoints { get => _cpuPoints; private set => SetPoints(ref _cpuPoints, value); }

    /// <summary>Memory in use over the last few minutes, for the row's mini-chart.</summary>
    public PointCollection MemoryPoints { get => _memoryPoints; private set => SetPoints(ref _memoryPoints, value); }

    public PointCollection NetRxPoints { get => _netRxPoints; private set => SetPoints(ref _netRxPoints, value); }

    public PointCollection NetTxPoints { get => _netTxPoints; private set => SetPoints(ref _netTxPoints, value); }

    public PointCollection DiskReadPoints { get => _diskReadPoints; private set => SetPoints(ref _diskReadPoints, value); }

    public PointCollection DiskWritePoints { get => _diskWritePoints; private set => SetPoints(ref _diskWritePoints, value); }

    /// <summary>Current receive/transmit rates and totals, for the network chart's tooltip.</summary>
    public string NetText { get => _netText; private set => Set(ref _netText, value); }

    /// <summary>Current read/write rates and totals, for the disk chart's tooltip.</summary>
    public string DiskText { get => _diskText; private set => Set(ref _diskText, value); }

    // Null rather than empty, so a stopped container shows no empty tooltip.
    public string? NetTip => _netText.Length > 0 ? _netText : null;

    public string? DiskTip => _diskText.Length > 0 ? _diskText : null;

    /// <summary>Restart policy and live status; empty when the container has none.</summary>
    public string Restart { get => _restart; private set => Set(ref _restart, value); }

    public Microsoft.UI.Xaml.Media.Brush RestartBrush => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
        _restartAlert ? "SystemFillColorCriticalBrush" : "TextFillColorSecondaryBrush"];

    public bool IsRunning => IsRunningState(_state);

    public bool IsStopped => !IsRunning;

    public Visibility RunningVisibility => IsRunning ? Visibility.Visible : Visibility.Collapsed;

    public Visibility StoppedVisibility => IsRunning ? Visibility.Collapsed : Visibility.Visible;

    public static bool IsRunningState(string state) =>
        state.Equals("running", StringComparison.OrdinalIgnoreCase);

    public void Update(ContainerRow row, IReadOnlyList<StatsSample> history, RestartInfo? restart = null)
    {
        Restart = IsThisApp ? RestartDisplay.Short(restart ?? RestartInfo.Empty, DateTimeOffset.UtcNow) : string.Empty;
        var alert = restart?.State == RestartState.CrashLoop;
        if (alert != _restartAlert)
        {
            _restartAlert = alert;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RestartBrush)));
        }

        Image = row.Image;
        State = row.State;
        ComposeLabels.TryParse(row.Labels, out var project, out var service);
        Stack = project;
        Service = service;
        UpdateTelemetry(history);
    }

    public void UpdateTelemetry(IReadOnlyList<StatsSample> history)
    {
        const double w = ContainerMetricsLayout.ChartWidth, h = ContainerMetricsLayout.ChartHeight;
        var sample = history.Count > 0 ? history[^1] : null;
        if (!IsRunning || sample is null)
        {
            Cpu = Memory = "–";
            NetText = DiskText = string.Empty;
            ClearCharts(cpuAndMemory: true);
            return;
        }

        Cpu = $"{sample.CpuPercent:0.#}%";
        Memory = WslcHost.FormatSize(sample.MemoryBytes);

        // Lines for collapsed cells aren't built; widening the list rebuilds them from the same history.
        CpuPoints = Columns.ShowsCharts ? ToPoints(Sparkline.Cpu(history, w, h)) : [];
        MemoryPoints = Columns.ShowsCharts ? ToPoints(Sparkline.Memory(history, w, h)) : [];
        if (Columns.ShowsIo)
        {
            var net = Sparkline.Io(history, s => s.NetRxBytes, s => s.NetTxBytes, w, h);
            var disk = Sparkline.Io(history, s => s.BlockReadBytes, s => s.BlockWriteBytes, w, h);
            (NetRxPoints, NetTxPoints) = (ToPoints(net.First), ToPoints(net.Second));
            (DiskReadPoints, DiskWritePoints) = (ToPoints(disk.First), ToPoints(disk.Second));
            NetText = UiStrings.Format("SSTotal", Size(net.LatestFirst), Size(net.LatestSecond), WslcHost.FormatSize(sample.NetRxBytes), WslcHost.FormatSize(sample.NetTxBytes));
            DiskText = UiStrings.Format("ReadSWriteSTotalReadWrite", Size(disk.LatestFirst), Size(disk.LatestSecond), WslcHost.FormatSize(sample.BlockReadBytes), WslcHost.FormatSize(sample.BlockWriteBytes));
        }
        else
        {
            ClearCharts(cpuAndMemory: false);
        }

        static string Size(double bytes) => WslcHost.FormatSize((ulong)Math.Max(0, bytes));
    }

    // Each line gets its own empty collection: a points collection shouldn't be shared between shapes.
    private void ClearCharts(bool cpuAndMemory)
    {
        if (cpuAndMemory)
        {
            CpuPoints = [];
            MemoryPoints = [];
        }

        NetRxPoints = [];
        NetTxPoints = [];
        DiskReadPoints = [];
        DiskWritePoints = [];
    }

    private static PointCollection ToPoints(IReadOnlyList<(double X, double Y)> points)
    {
        var collection = new PointCollection();
        foreach (var (x, y) in points)
        {
            collection.Add(new Windows.Foundation.Point(x, y));
        }

        return collection;
    }

    private void SetPoints(ref PointCollection field, PointCollection value, [CallerMemberName] string? property = null)
    {
        // An empty line stays empty; skipping the notification avoids re-rendering idle rows.
        if (field.Count == 0 && value.Count == 0)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }

    private void Set(ref string field, string value, [CallerMemberName] string? property = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        if (property == nameof(Header))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HeaderVisibility)));
        }

        if (property is nameof(Image) or nameof(Service))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImageText)));
        }

        if (property is nameof(NetText) or nameof(DiskText))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property == nameof(NetText) ? nameof(NetTip) : nameof(DiskTip)));
        }

        if (property == nameof(State))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRunning)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsStopped)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RunningVisibility)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StoppedVisibility)));
        }
    }
}
