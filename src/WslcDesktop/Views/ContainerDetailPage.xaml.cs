using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;
using WslcDesktop.Services;

namespace WslcDesktop.Views;

public sealed partial class ContainerDetailPage : Page
{
    private string _name = string.Empty;
    private bool _ready;
    private CancellationTokenSource? _logCts;
    private ShellProcess? _shell;

    public ContainerDetailPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _ready = true;
            App.Host.Telemetry.Updated += OnTelemetryUpdated;
            StartLogs();
            RenderTelemetry();
        };
        Unloaded += (_, _) =>
        {
            App.Host.Telemetry.Updated -= OnTelemetryUpdated;
            _logCts?.Cancel();
            StopShell();
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _name = (string)e.Parameter;
        TitleText.Text = _name;
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
        else
        {
            Frame.Navigate(typeof(ContainersPage));
        }
    }

    // Logs

    private void StartLogs()
    {
        if (!_ready)
        {
            return;
        }

        _logCts?.Cancel();
        var cts = _logCts = new CancellationTokenSource();
        LogView.Clear();
        LogView.AutoScroll = FollowToggle.IsOn;
        var tail = double.IsNaN(TailBox.Value) ? 200 : (int)TailBox.Value;
        var follow = FollowToggle.IsOn;
        var timestamps = TimestampToggle.IsOn;
        LogStatus.Text = follow ? "Streaming…" : "Loading…";

        _ = Task.Run(async () =>
        {
            string? error = null;
            try
            {
                await App.Host.StreamLogsAsync(_name, tail, follow, timestamps, LogView.Append, cts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            if (!cts.IsCancellationRequested)
            {
                DispatcherQueue.TryEnqueue(() =>
                    LogStatus.Text = error ?? (follow ? "Stream ended (container stopped?)" : "Done"));
            }
        });
    }

    private void LogOptions_Changed(object sender, RoutedEventArgs e) => StartLogs();

    private void TailBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => StartLogs();

    private void ClearLogs_Click(object sender, RoutedEventArgs e) => LogView.Clear();

    // Shell

    private void ShellToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_shell is not null)
        {
            StopShell();
            return;
        }

        ShellView.Clear();
        var shell = ShellPicker.SelectedItem as string ?? "sh";
        try
        {
            ShellProcess? started = null;
            started = App.Host.StartShell(_name, shell, ShellView.Append, code => DispatcherQueue.TryEnqueue(() =>
            {
                if (ReferenceEquals(_shell, started))
                {
                    ShellView.Append($"[shell exited with code {code}]");
                    StopShell();
                }
            }));
            _shell = started;
        }
        catch (Exception ex)
        {
            ShellStatus.Text = ex.Message;
            return;
        }

        ShellToggle.Content = "End shell";
        ShellStatus.Text = $"Connected to {_name} (no TTY: full-screen programs aren't supported)";
        ShellInput.IsEnabled = true;
        ShellInput.Focus(FocusState.Programmatic);
    }

    private void StopShell()
    {
        _shell?.Dispose();
        _shell = null;
        ShellToggle.Content = "Start shell";
        ShellInput.IsEnabled = false;
        ShellStatus.Text = string.Empty;
    }

    private async void ShellInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || _shell is null)
        {
            return;
        }

        e.Handled = true;
        var line = ShellInput.Text;
        ShellInput.Text = string.Empty;
        ShellView.Append("$ " + line);
        await _shell.SendLineAsync(line);
    }

    // Telemetry

    private void OnTelemetryUpdated() => DispatcherQueue.TryEnqueue(RenderTelemetry);

    private void Chart_SizeChanged(object sender, SizeChangedEventArgs e) => RenderTelemetry();

    private void RenderTelemetry()
    {
        var history = App.Host.Telemetry.GetHistory(_name);
        if (history.Count == 0)
        {
            TelemetryNotice.IsOpen = true;
            TelemetryNotice.Message = App.Host.Telemetry.LastError
                ?? "Waiting for the first sample. Telemetry is only available while the container is running.";
            return;
        }

        TelemetryNotice.IsOpen = false;
        var last = history[^1];
        CpuValue.Text = $"{last.CpuPercent:0.##}%";
        MemValue.Text = $"{WslcHost.FormatSize(last.MemoryBytes)} of {WslcHost.FormatSize(last.MemoryLimitBytes)} ({last.MemoryPercent:0.##}%)";

        var cpu = history.Select(s => s.CpuPercent).ToList();
        Plot(CpuHost, CpuLine, cpu, Math.Max(10, cpu.Max()));
        Plot(MemHost, MemLine, history.Select(s => s.MemoryPercent).ToList(), 100);

        var rx = Rates(history, s => s.NetRxBytes);
        var tx = Rates(history, s => s.NetTxBytes);
        var netMax = Math.Max(1024, Math.Max(rx.Max(), tx.Max()));
        Plot(NetHost, NetRxLine, rx, netMax);
        Plot(NetHost, NetTxLine, tx, netMax);
        NetValue.Text = $"↓ {WslcHost.FormatSize((ulong)rx[^1])}/s  ↑ {WslcHost.FormatSize((ulong)tx[^1])}/s · total ↓ {WslcHost.FormatSize(last.NetRxBytes)}  ↑ {WslcHost.FormatSize(last.NetTxBytes)}";

        var rd = Rates(history, s => s.BlockReadBytes);
        var wr = Rates(history, s => s.BlockWriteBytes);
        var blockMax = Math.Max(1024, Math.Max(rd.Max(), wr.Max()));
        Plot(BlockHost, BlockReadLine, rd, blockMax);
        Plot(BlockHost, BlockWriteLine, wr, blockMax);
        BlockValue.Text = $"read {WslcHost.FormatSize((ulong)rd[^1])}/s  write {WslcHost.FormatSize((ulong)wr[^1])}/s · total read {WslcHost.FormatSize(last.BlockReadBytes)}  write {WslcHost.FormatSize(last.BlockWriteBytes)}";
        PidsValue.Text = $"{last.Pids} processes · read (blue), write (amber), per second";
    }

    private static List<double> Rates(IReadOnlyList<StatsSample> history, Func<StatsSample, ulong> pick)
    {
        var rates = new List<double>(history.Count) { 0 };
        for (var i = 1; i < history.Count; i++)
        {
            var seconds = (history[i].Time - history[i - 1].Time).TotalSeconds;
            var delta = pick(history[i]) >= pick(history[i - 1]) ? pick(history[i]) - pick(history[i - 1]) : 0UL;
            rates.Add(seconds > 0 ? delta / seconds : 0);
        }

        return rates;
    }

    private static void Plot(Grid host, Microsoft.UI.Xaml.Shapes.Polyline line, IReadOnlyList<double> values, double max)
    {
        var width = host.ActualWidth;
        var height = host.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var step = width / (TelemetryService.Capacity - 1);
        var points = new PointCollection();
        for (var i = 0; i < values.Count; i++)
        {
            var x = width - (values.Count - 1 - i) * step;
            var y = height - Math.Clamp(values[i] / max, 0, 1) * (height - 2) - 1;
            points.Add(new Windows.Foundation.Point(x, y));
        }

        line.Points = points;
    }
}
