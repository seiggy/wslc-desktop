using WslcDesktop.Cli;
using WslcDesktop.Containers;

namespace WslcDesktop.Services;

/// <summary>
/// Polls <c>wslc stats</c> for all running containers and keeps a rolling in-memory history per container,
/// so charts have data even for the time before the user opened a container.
/// </summary>
public sealed class TelemetryService(WslcCli cli)
{
    public const int Capacity = 150;
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly Dictionary<string, List<StatsSample>> _history = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;

    public event Action? Updated;

    // Charts aren't visible while the window is hidden, so sampling pauses.
    public volatile bool Paused;
    public string? LastError { get; private set; }

    public void Start(string session)
    {
        if (_cts is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _ = LoopAsync(session, _cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    public IReadOnlyList<StatsSample> GetHistory(string container)
    {
        lock (_gate)
        {
            return _history.TryGetValue(container, out var list) ? list.ToArray() : Array.Empty<StatsSample>();
        }
    }

    public StatsSample? GetLatest(string container)
    {
        lock (_gate)
        {
            return _history.TryGetValue(container, out var list) && list.Count > 0 ? list[^1] : null;
        }
    }

    private async Task LoopAsync(string session, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            if (Paused)
            {
                continue;
            }

            try
            {
                var snapshot = await cli.GetStatsAsync(session, ct);
                lock (_gate)
                {
                    foreach (var name in _history.Keys.Where(k => !snapshot.ContainsKey(k)).ToList())
                    {
                        // A stopped container's old samples would mislead the chart on restart.
                        _history.Remove(name);
                    }

                    foreach (var (name, sample) in snapshot)
                    {
                        if (!_history.TryGetValue(name, out var list))
                        {
                            _history[name] = list = [];
                        }

                        list.Add(sample);
                        if (list.Count > Capacity)
                        {
                            list.RemoveAt(0);
                        }
                    }
                }

                LastError = null;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                LastError = e.Message;
            }

            if (!ct.IsCancellationRequested)
            {
                Updated?.Invoke();
            }
        }
        while (await WaitAsync(timer, ct));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
