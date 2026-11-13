using WslcDesktop.Cli;

namespace WslcDesktop.Data;

/// <summary>
/// Keeps one <c>wslc events</c> stream open per visible session, so changes made by any client (this app, the CLI,
/// other tools) arrive as they happen instead of on the next poll. A stream that ends (the session was terminated,
/// wslc failed) reconnects with backoff; one that keeps failing straight away is retried rarely.
/// </summary>
public sealed class SessionEventStreams(
    IWslcProcess process,
    Action<string, WslcEvent> onEvent,
    Action<string, StreamState> onState,
    TimeProvider? time = null) : IDisposable
{
    /// <summary>A stream that ran this long may have missed events while it was down, so its session is re-read.</summary>
    public static readonly TimeSpan Established = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan GiveUpBackoff = TimeSpan.FromMinutes(10);
    private const int QuickFailuresBeforeGivingUp = 3;

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Dictionary<string, CancellationTokenSource> _streams = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _live = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public bool IsLive(string session)
    {
        lock (_live)
        {
            return _live.Contains(session);
        }
    }

    /// <summary>Opens streams for new sessions and closes those for sessions that are gone.</summary>
    public void Sync(IEnumerable<string> sessions)
    {
        var wanted = sessions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var started = new List<(string Session, CancellationToken Token)>();
        lock (_streams)
        {
            if (_disposed)
            {
                return;
            }

            foreach (var gone in _streams.Keys.Where(s => !wanted.Contains(s)).ToList())
            {
                _streams[gone].Cancel();
                _streams.Remove(gone);
            }

            foreach (var session in wanted.Where(s => !_streams.ContainsKey(s)))
            {
                var cts = new CancellationTokenSource();
                _streams[session] = cts;
                started.Add((session, cts.Token));
            }
        }

        // Started outside the lock and off the caller's thread: callbacks may come straight back into the store.
        foreach (var (session, token) in started)
        {
            _ = Task.Run(() => RunAsync(session, token));
        }
    }

    public void Dispose()
    {
        lock (_streams)
        {
            _disposed = true;
            foreach (var cts in _streams.Values)
            {
                cts.Cancel();
            }

            _streams.Clear();
        }
    }

    private async Task RunAsync(string session, CancellationToken ct)
    {
        var backoff = MinBackoff;
        var quickFailures = 0;
        var connectedBefore = false;
        while (!ct.IsCancellationRequested)
        {
            var started = _time.GetUtcNow();
            SetLive(session, true);
            onState(session, connectedBefore ? StreamState.Reconnected : StreamState.Connected);
            connectedBefore = true;
            try
            {
                await process.StreamAsync(
                    new WslcInvocation(["--session", session, "events", "--format", "json"]),
                    line =>
                    {
                        if (WslcEvent.Parse(line) is { } e)
                        {
                            onEvent(session, e);
                        }
                    },
                    ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                // Treated like any other end of the stream.
            }

            SetLive(session, false);
            if (ct.IsCancellationRequested)
            {
                break;
            }

            var ranFor = _time.GetUtcNow() - started;
            onState(session, ranFor >= Established ? StreamState.Lost : StreamState.Failed);
            if (ranFor >= Established)
            {
                (backoff, quickFailures) = (MinBackoff, 0);
            }
            else
            {
                quickFailures++;
                backoff = quickFailures >= QuickFailuresBeforeGivingUp
                    ? GiveUpBackoff
                    : TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
            }

            try
            {
                await Task.Delay(backoff, _time, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        SetLive(session, false);
    }

    private void SetLive(string session, bool live)
    {
        lock (_live)
        {
            if (live)
            {
                _live.Add(session);
            }
            else
            {
                _live.Remove(session);
            }
        }
    }
}

public enum StreamState
{
    /// <summary>First connection for the session.</summary>
    Connected,

    /// <summary>Connected again after a gap: events may have been missed.</summary>
    Reconnected,

    /// <summary>An established stream ended: the session may be gone, or events were missed.</summary>
    Lost,

    /// <summary>The stream ended straight after starting (unsupported, or the session is gone).</summary>
    Failed,
}
