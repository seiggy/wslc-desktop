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
    private string _session = string.Empty;
    private readonly DispatcherTimer _otherSessionTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    private bool IsAppSession => App.Host.IsAppSession(_session);

    private bool HasLoaded => IsAppSession ? App.Host.Monitor.HasLoaded : App.Host.ContainerCatalog.Latest is not null;

    private ContainerRow? FindRow() => IsAppSession
        ? App.Host.Monitor.Find(_name)
        : App.Host.ContainerCatalog.Latest?.Items
            .FirstOrDefault(e => string.Equals(e.Session, _session, StringComparison.OrdinalIgnoreCase) && e.Item.Name == _name)?.Item;
    private bool _ready;
    private CancellationTokenSource? _logCts;
    private ShellProcess? _shell;

    public ContainerDetailPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _ready = true;
            ApplyTerminalFont();
            App.Host.Settings.TerminalChanged += ApplyTerminalFont;
            App.Host.Telemetry.Updated += OnTelemetryUpdated;
            App.Host.Monitor.Updated += OnTelemetryUpdated;
            if (!IsAppSession)
            {
                // Other sessions aren't polled by the monitor, so this page keeps its container current.
                _otherSessionTimer.Tick += async (_, _) =>
                {
                    try
                    {
                        await App.Host.ContainerCatalog.RefreshAsync();
                    }
                    catch
                    {
                        return;
                    }

                    OnTelemetryUpdated();
                };
                _otherSessionTimer.Start();
                _ = App.Host.ContainerCatalog.RefreshAsync().ContinueWith(_ => DispatcherQueue.TryEnqueue(ApplyState));
            }

            _wasRunning = IsRunning;
            LoadPolicy();
            RenderRestartStatus();
            StartLogs();
            ApplyState();
            RenderTelemetry();
            _ = LoadInspectAsync();
        };
        Unloaded += (_, _) =>
        {
            App.Host.Telemetry.Updated -= OnTelemetryUpdated;
            App.Host.Monitor.Updated -= OnTelemetryUpdated;
            _otherSessionTimer.Stop();
            App.Host.Settings.TerminalChanged -= ApplyTerminalFont;
            _logCts?.Cancel();
            StopShell();
        };
    }

    private bool _wasRunning;
    private bool _syncingPolicy;

    private void LoadPolicy()
    {
        if (!IsAppSession)
        {
            return;
        }

        var policy = App.Host.Restart.GetPolicy(_name);
        _syncingPolicy = true;
        RestartPicker.ItemsSource = RestartDisplay.ModeLabels;
        RestartPicker.SelectedIndex = RestartDisplay.ModeIndex(policy);
        RestartRetries.Value = policy.MaxRetries;
        RestartRetries.Visibility = policy.Mode == RestartMode.OnFailure ? Visibility.Visible : Visibility.Collapsed;
        _syncingPolicy = false;
    }

    private void RestartPicker_Changed(object sender, object e)
    {
        if (_syncingPolicy || !_ready)
        {
            return;
        }

        var retries = double.IsNaN(RestartRetries.Value) ? 0 : (int)RestartRetries.Value;
        var policy = RestartDisplay.FromSelection(RestartPicker.SelectedIndex, retries);
        RestartRetries.Visibility = policy.Mode == RestartMode.OnFailure ? Visibility.Visible : Visibility.Collapsed;
        if (!IsAppSession)
        {
            return;
        }

        App.Host.SetRestartPolicy(_name, policy, IsRunning);
        RenderRestartStatus();
    }

    private void RenderRestartStatus() =>
        RestartStatus.Text = IsAppSession ? RestartDisplay.Long(App.Host.Restart.GetInfo(_name), DateTimeOffset.UtcNow) : string.Empty;

    private bool IsRunning => FindRow() is { } row && ContainerItem.IsRunningState(row.State);

    private void ApplyState()
    {
        var row = FindRow();
        var running = IsRunning;
        TitleText.Text = row is null ? _name : $"{_name} · {row.State}";

        ShellPanel.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        ShellStoppedNotice.IsOpen = !running && HasLoaded;
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
        RenderRestartStatus();
    }

    private async void StartContainer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await App.Host.StartContainerAsync(_name, _session);
        }
        catch (Exception ex)
        {
            ShellStoppedNotice.Message = ex.Message;
        }

        await App.Host.Monitor.RefreshAsync();
    }

    private async void Inspect_Click(object sender, RoutedEventArgs e) => await LoadInspectAsync();

    private async void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        var network = (string)((FrameworkElement)sender).Tag;
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Disconnect from \"{network}\"?",
            Content = $"{_name} will lose access to this network.",
            PrimaryButtonText = "Disconnect",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
        {
            await NetworkActionAsync("Couldn't disconnect", () => App.Host.DisconnectNetworkAsync(network, _name, _session));
        }
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<string> all;
        try
        {
            all = await App.Host.GetNetworkNamesAsync(_session);
        }
        catch (Exception ex)
        {
            InspectError.Message = ex.Message;
            InspectError.IsOpen = true;
            return;
        }

        var connected = (await App.Host.InspectContainerAsync(_name, _session)) is { } json
            ? ContainerDetail.Parse(json).Networks.Select(n => n.Name).ToHashSet()
            : [];
        var choices = all.Where(n => !connected.Contains(n)).ToList();
        if (choices.Count == 0)
        {
            await new ContentDialog { XamlRoot = XamlRoot, Title = "No networks available", Content = "This container is already connected to every network.", CloseButtonText = "Close" }.ShowAsync();
            return;
        }

        var network = new ComboBox { Header = "Network", ItemsSource = choices, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var alias = new TextBox { Header = "Alias (optional)" };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Connect {_name} to a network",
            Content = new StackPanel { Spacing = 12, MinWidth = 360, Children = { network, alias } },
            PrimaryButtonText = "Connect",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && network.SelectedItem is string chosen)
        {
            await NetworkActionAsync("Couldn't connect", () => App.Host.ConnectNetworkAsync(chosen, _name, alias.Text.Trim(), _session));
        }
    }

    private async Task NetworkActionAsync(string failureTitle, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            await new ContentDialog { XamlRoot = XamlRoot, Title = failureTitle, Content = ex.Message, CloseButtonText = "Close" }.ShowAsync();
        }

        await LoadInspectAsync();
    }

    private async Task LoadInspectAsync()
    {
        InspectError.IsOpen = false;
        try
        {
            var json = await App.Host.InspectContainerAsync(_name, _session);
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
            var running = string.Equals(d.Status, "running", StringComparison.OrdinalIgnoreCase);
            if (!running)
            {
                Controls.InspectUi.AddRow(rows, "Finished", Controls.InspectUi.FormatDate(d.Finished));
                Controls.InspectUi.AddRow(rows, "Exit code", d.ExitCode);
            }
            Controls.InspectUi.AddRow(rows, "Memory limit", d.MemoryLimit.Length > 0 ? d.MemoryLimit : "Unlimited");
            Controls.InspectUi.AddRow(rows, "CPU limit", d.CpuLimit.Length > 0 ? d.CpuLimit : "Unlimited");
            InspectPanel.Children.Add(card);

            card = Controls.InspectUi.Card("Ports and networking", out rows);
            Controls.InspectUi.AddRow(rows, "Network mode", d.NetworkMode);
            Controls.InspectUi.AddRow(rows, "Ports", d.Ports.Count > 0 ? string.Join('\n', d.Ports) : "None");
            foreach (var n in d.Networks)
            {
                var row = new Grid { ColumnSpacing = 16 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(Controls.InspectUi.Secondary($"Network {n.Name}"));
                var info = Controls.InspectUi.Mono(string.Join('\n', new[] { n.IpAddress, n.Gateway.Length > 0 ? $"Gateway {n.Gateway}" : "", n.Mac }.Where(s => s.Length > 0)));
                Grid.SetColumn(info, 1);
                row.Children.Add(info);
                var disconnect = new Button { Content = "Disconnect", Tag = n.Name, VerticalAlignment = VerticalAlignment.Top };
                disconnect.Click += Disconnect_Click;
                Grid.SetColumn(disconnect, 2);
                row.Children.Add(disconnect);
                rows.Children.Add(row);
            }

            var connect = new Button { Content = "Connect to network…" };
            connect.Click += Connect_Click;
            rows.Children.Add(connect);
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
        (_session, _name) = e.Parameter is ContainerRef r ? (r.Session, r.Name) : (App.Host.SessionId, (string)e.Parameter);
        TitleText.Text = _name;
        SessionText.Text = IsAppSession ? string.Empty : $"Session: {_session}";
        if (!IsAppSession)
        {
            RestartPicker.Visibility = RestartRetries.Visibility = RestartStatus.Visibility = Visibility.Collapsed;
        }
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
                await App.Host.StreamLogsAsync(_name, tail, follow, timestamps, LogView.Append, cts.Token, _session);
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

    private bool _terminalActive;

    private void ApplyTerminalFont() => Terminal.SetFont(App.Host.Settings.TerminalFontFamily, App.Host.Settings.TerminalFontSize);

    private bool ShellRunning => _terminalActive || _shell is not null;

    private async void ShellToggle_Click(object sender, RoutedEventArgs e)
    {
        if (ShellRunning)
        {
            StopShell();
            return;
        }

        var shell = ShellPicker.SelectedItem as string ?? "sh";
        ShellToggle.IsEnabled = false;
        try
        {
            if (await TryStartTerminalAsync(shell))
            {
                return;
            }

            StartFallbackShell(shell);
        }
        finally
        {
            ShellToggle.IsEnabled = true;
        }
    }

    /// <summary>Starts the ConPTY terminal; returns false (so the caller falls back) if it can't initialise.</summary>
    private async Task<bool> TryStartTerminalAsync(string shell)
    {
        Terminal.Visibility = Visibility.Visible;
        ShellView.Visibility = Visibility.Collapsed;
        ShellInput.Visibility = Visibility.Collapsed;
        try
        {
            await Terminal.StartAsync(App.Host.SessionId, _name, shell, code => DispatcherQueue.TryEnqueue(() =>
            {
                if (_terminalActive)
                {
                    Terminal.Notice($"[shell exited with code {code}]");
                    StopShell(keepOutput: true);
                }
            }));
        }
        catch (Exception ex)
        {
            Terminal.Stop();
            Terminal.Visibility = Visibility.Collapsed;
            ShellView.Visibility = Visibility.Visible;
            ShellInput.Visibility = Visibility.Visible;
            ShellStatus.Text = $"Terminal unavailable ({ex.GetType().Name}: {ex.Message}); using the basic shell.";
            LogBackend($"fallback: {ex}");
            return false;
        }

        _terminalActive = true;
        ShellToggle.Content = "End shell";
        ShellStatus.Text = $"Connected to {_name}";
        LogBackend("native terminal active");
        return true;
    }

    private static readonly System.Text.RegularExpressions.Regex s_ansi =
        new("\u001b\\[[0-9;?]*[ -/]*[@-~]|\\[[0-9;?]*m", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static void LogBackend(string message)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WslcDesktop");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "terminal.log"), $"{DateTime.Now:s} {message}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
    }

    private void StartFallbackShell(string shell)
    {
        ShellView.Clear();
        LogBackend("basic piped shell active");
        try
        {
            ShellProcess? started = null;
            started = App.Host.StartShell(_name, shell, line => ShellView.Append(s_ansi.Replace(line, string.Empty)), code => DispatcherQueue.TryEnqueue(() =>
            {
                if (ReferenceEquals(_shell, started))
                {
                    ShellView.Append($"[shell exited with code {code}]");
                    StopShell();
                }
            }), _session);
            _shell = started;
        }
        catch (Exception ex)
        {
            ShellStatus.Text = ex.Message;
            return;
        }

        ShellToggle.Content = "End shell";
        ShellStatus.Text = $"Connected to {_name} (basic shell, no TTY: full-screen programs aren't supported)";
        ShellInput.IsEnabled = true;
        ShellInput.Focus(FocusState.Programmatic);
    }

    private void StopShell(bool keepOutput = false)
    {
        _shell?.Dispose();
        _shell = null;
        if (_terminalActive)
        {
            Terminal.Stop();
            _terminalActive = false;
        }

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
        if (!IsAppSession)
        {
            TelemetryNotice.IsOpen = true;
            TelemetryNotice.Message = $"Telemetry is only collected for containers in this app's session (\"{App.Host.SessionId}\").";
            return;
        }

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

    private void DetailPivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // The native terminal window ignores XAML tab clipping, so collapse its panel on other tabs.
        ShellPanel.Visibility = ReferenceEquals(DetailPivot.SelectedItem, ShellTabItem) ? Visibility.Visible : Visibility.Collapsed;
    }
}
