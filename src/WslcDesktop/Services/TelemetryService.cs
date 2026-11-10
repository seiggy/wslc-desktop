using WslcDesktop.Cli;
using WslcDesktop.Containers;

namespace WslcDesktop.Services;

/// <summary>
/// Polls <c>wslc stats</c> for the running containers of every visible session and keeps a rolling in-memory history
/// per container, so charts have data even for the time before the user opened a container.
/// </summary>
public sealed class TelemetryService(WslcCli cli, Func<Task<IReadOnlyList<string>>> listSessions)
{
    public const int Capacity = 150;
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SessionListInterval = TimeSpan.FromSeconds(10);

    private readonly Dictionary<string, List<StatsSample>> _history = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private IReadOnlyList<string> _sessions = [];
    private DateTimeOffset _sessionsAt;

    public event Action? Updated;

    // Charts aren't visible while the window is hidden, so sampling pauses.
    public volatile bool Paused;
    public string? LastError { get; private set; }

    public void Start()
    {
        if (_cts is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _ = LoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    private static string Key(string session, string container) => $"{session}|{container}";

    public IReadOnlyList<StatsSample> GetHistory(string session, string container)
    {
        lock (_gate)
        {
            return _history.TryGetValue(Key(session, container), out var list) ? list.ToArray() : Array.Empty<StatsSample>();
        }
    }

    public StatsSample? GetLatest(string session, string container)
    {
        lock (_gate)
        {
            return _history.TryGetValue(Key(session, container), out var list) && list.Count > 0 ? list[^1] : null;
        }
    }

    private async Task<IReadOnlyList<string>> SessionsAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (_sessions.Count == 0 || now - _sessionsAt >= SessionListInterval)
        {
            _sessions = await listSessions().WaitAsync(ct).ConfigureAwait(false);
            _sessionsAt = now;
        }

        return _sessions;
    }

    private async Task SampleAsync(CancellationToken ct)
    {
        var sessions = await SessionsAsync(ct).ConfigureAwait(false);
        var results = await Task.WhenAll(sessions.Select(async s =>
        {
            try
            {
                return (Session: s, Snapshot: (IReadOnlyDictionary<string, StatsSample>?)await cli.GetStatsAsync(s, ct).ConfigureAwait(false), Error: (string?)null);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                return (s, null, e.Message);
            }
        })).ConfigureAwait(false);

        lock (_gate)
        {
            var sampled = results.Where(r => r.Snapshot is not null).Select(r => r.Session).ToHashSet(StringComparer.Ordinal);
            var live = results.Where(r => r.Snapshot is not null)
                .SelectMany(r => r.Snapshot!.Keys.Select(name => Key(r.Session, name)))
                .ToHashSet(StringComparer.Ordinal);
            // A stopped container's old samples would mislead the chart on restart; failed sessions keep theirs.
            foreach (var key in _history.Keys.Where(k => !live.Contains(k) && (sampled.Contains(k[..k.IndexOf('|')]) || !sessions.Contains(k[..k.IndexOf('|')]))).ToList())
            {
                _history.Remove(key);
            }

            foreach (var r in results.Where(r => r.Snapshot is not null))
            {
                foreach (var (name, sample) in r.Snapshot!)
                {
                    var key = Key(r.Session, name);
                    if (!_history.TryGetValue(key, out var list))
                    {
                        _history[key] = list = [];
                    }

                    list.Add(sample);
                    if (list.Count > Capacity)
                    {
                        list.RemoveAt(0);
                    }
                }
            }
        }

        LastError = results.FirstOrDefault(r => r.Error is not null).Error;
    }

    private async Task LoopAsync(CancellationToken ct)
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
                await SampleAsync(ct).ConfigureAwait(false);
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
        while (await WaitAsync(timer, ct).ConfigureAwait(false));
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
