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
            App.Host.Monitor.Updated += OnTelemetryUpdated;
            _wasRunning = IsRunning;
            StartLogs();
            ApplyState();
            RenderTelemetry();
            _ = LoadInspectAsync();
        };
        Unloaded += (_, _) =>
        {
            App.Host.Telemetry.Updated -= OnTelemetryUpdated;
            App.Host.Monitor.Updated -= OnTelemetryUpdated;
            _logCts?.Cancel();
            StopShell();
        };
    }

    private bool _wasRunning;

    private bool IsRunning => App.Host.Monitor.Find(_name) is { } row && ContainerItem.IsRunningState(row.State);

    private void ApplyState()
    {
        var row = App.Host.Monitor.Find(_name);
        var running = IsRunning;
        TitleText.Text = row is null ? _name : $"{_name} · {row.State}";

        ShellPanel.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        ShellStoppedNotice.IsOpen = !running && App.Host.Monitor.HasLoaded;
        if (!running && _shell is not null)
        {
            ShellView.Append("[container stopped]");
            StopShell();
        }

        // Pick up new output when a stopped container starts again.
        if (running && !_wasRunning)
        {
            StartLogs();
        }

        _wasRunning = running;
    }

    private async void StartContainer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await App.Host.StartContainerAsync(_name);
        }
        catch (Exception ex)
        {
            ShellStoppedNotice.Message = ex.Message;
        }

        await App.Host.Monitor.RefreshAsync();
    }

    private async void Inspect_Click(object sender, RoutedEventArgs e) => await LoadInspectAsync();

    private async Task LoadInspectAsync()
    {
        InspectError.IsOpen = false;
        try
        {
            var json = await App.Host.InspectContainerAsync(_name);
            var d = ContainerDetail.Parse(json);
            InspectPanel.Children.Clear();

            var card = Controls.InspectUi.Card("Overview", out var rows);
            Controls.InspectUi.AddRow(rows, "Name", d.Name);
            Controls.InspectUi.AddRow(rows, "ID", d.Id, true);
            Controls.InspectUi.AddRow(rows, "Image", d.Image);
            Controls.InspectUi.AddRow(rows, "Image ID", d.ImageId, true);
            Controls.InspectUi.AddRow(rows, "Status", d.Status);
            Controls.InspectUi.AddRow(rows, "Health", d.Health);
            Controls.InspectUi.AddRow(rows, "Created", Controls.InspectUi.FormatDate(d.Created));
            Controls.InspectUi.AddRow(rows, "Started", Controls.InspectUi.FormatDate(d.Started));
            Controls.InspectUi.AddRow(rows, "Finished", Controls.InspectUi.FormatDate(d.Finished));
            Controls.InspectUi.AddRow(rows, "Exit code", d.ExitCode);
            Controls.InspectUi.AddRow(rows, "Memory limit", d.MemoryLimit.Length > 0 ? d.MemoryLimit : "Unlimited");
            Controls.InspectUi.AddRow(rows, "CPU limit", d.CpuLimit.Length > 0 ? d.CpuLimit : "Unlimited");
            InspectPanel.Children.Add(card);

            card = Controls.InspectUi.Card("Ports and networking", out rows);
            Controls.InspectUi.AddRow(rows, "Network mode", d.NetworkMode);
            Controls.InspectUi.AddRow(rows, "Ports", d.Ports.Count > 0 ? string.Join('\n', d.Ports) : "None");
            foreach (var n in d.Networks)
            {
                Controls.InspectUi.AddRow(rows, $"Network {n.Name}",
                    string.Join('\n', new[] { n.IpAddress, n.Gateway.Length > 0 ? $"Gateway {n.Gateway}" : "", n.Mac }.Where(s => s.Length > 0)), true);
            }

            InspectPanel.Children.Add(card);

            card = Controls.InspectUi.Card("Mounts", out rows);
            Controls.InspectUi.AddRow(rows, "Mounts", d.Mounts.Count > 0 ? string.Join('\n', d.Mounts) : "None");
            InspectPanel.Children.Add(card);

            card = Controls.InspectUi.Card("Configuration", out rows);
            Controls.InspectUi.AddRow(rows, "Entrypoint", string.Join(' ', d.Entrypoint));
            Controls.InspectUi.AddRow(rows, "Command", string.Join(' ', d.Cmd));
            Controls.InspectUi.AddRow(rows, "Working dir", d.WorkingDir);
            Controls.InspectUi.AddRow(rows, "User", d.User);
            Controls.InspectUi.AddRow(rows, "Environment", d.Env.Count > 0 ? string.Join('\n', d.Env) : "None", true);
            Controls.InspectUi.AddRow(rows, "Labels", d.Labels.Count > 0 ? string.Join('\n', d.Labels) : "None", true);
            InspectPanel.Children.Add(card);

            InspectRaw.SetJson(json);
        }
        catch (Exception ex)
        {
            InspectError.Message = ex.Message;
            InspectError.IsOpen = true;
        }
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

    private void OnTelemetryUpdated() => DispatcherQueue.TryEnqueue(() =>
    {
        ApplyState();
        RenderTelemetry();
    });

    private void Chart_SizeChanged(object sender, SizeChangedEventArgs e) => RenderTelemetry();

    private void RenderTelemetry()
    {
        var history = App.Host.Telemetry.GetHistory(_name);
        if (history.Count == 0 || !IsRunning)
        {
            TelemetryNotice.IsOpen = true;
            TelemetryNotice.Message = App.Host.Telemetry.LastError ?? (IsRunning
                ? "Waiting for the first sample."
                : "This container is stopped. Telemetry is captured while it runs.");
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
