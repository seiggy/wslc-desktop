namespace WslcDesktop.Data;

/// <summary>
/// Runs every feed's background refreshes from one loop: whatever is due is read, at most
/// <c>maxConcurrent</c> at a time (each read is usually a wslc.exe launch), and the loop sleeps until the next feed is
/// due or a feed is marked dirty or watched.
/// </summary>
public sealed class FeedScheduler(TimeProvider time, int maxConcurrent = 3) : IDisposable
{
    private static readonly TimeSpan MaxSleep = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MinSleep = TimeSpan.FromMilliseconds(20);

    private readonly SemaphoreSlim _reads = new(maxConcurrent, maxConcurrent);
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly object _lock = new();
    private readonly List<IScheduledFeed> _feeds = [];
    private readonly HashSet<IScheduledFeed> _running = [];
    private CancellationTokenSource? _cts;
    private volatile bool _hidden;

    /// <summary>True while the window is hidden in the tray: feeds use their (slower) hidden intervals.</summary>
    public bool Hidden
    {
        get => _hidden;
        set
        {
            _hidden = value;
            Wake();
        }
    }

    public void Add(IScheduledFeed feed)
    {
        lock (_lock)
        {
            _feeds.Add(feed);
        }

        feed.Wake = Wake;
        Wake();
    }

    public void Remove(IScheduledFeed feed)
    {
        lock (_lock)
        {
            _feeds.Remove(feed);
        }

        feed.Wake = null;
    }

    public void Wake()
    {
        try
        {
            if (_wake.CurrentCount == 0)
            {
                _wake.Release();
            }
        }
        catch (SemaphoreFullException)
        {
            // Another caller woke the loop first.
        }
    }

    /// <summary>Starts every due feed that isn't already being read; completes when those reads finish.</summary>
    public Task RunDueAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        List<IScheduledFeed> due;
        lock (_lock)
        {
            due = _feeds.Where(f => !_running.Contains(f) && f.NextDue(now, _hidden) <= now).ToList();
            foreach (var feed in due)
            {
                _running.Add(feed);
            }
        }

        return due.Count == 0 ? Task.CompletedTask : Task.WhenAll(due.Select(f => RunOneAsync(f, ct)));
    }

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

    public void Dispose() => Stop();

    private async Task RunOneAsync(IScheduledFeed feed, CancellationToken ct)
    {
        try
        {
            await _reads.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await feed.RefreshAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                _reads.Release();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            // Feeds record their own failures; one bad read must not stop the loop.
        }
        finally
        {
            lock (_lock)
            {
                _running.Remove(feed);
            }

            Wake();
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            _ = RunDueAsync(ct);
            try
            {
                await _wake.WaitAsync(NextSleep(), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private TimeSpan NextSleep()
    {
        var now = time.GetUtcNow();
        var next = DateTimeOffset.MaxValue;
        lock (_lock)
        {
            foreach (var feed in _feeds)
            {
                if (!_running.Contains(feed))
                {
                    var due = feed.NextDue(now, _hidden);
                    if (due < next)
                    {
                        next = due;
                    }
                }
            }
        }

        if (next == DateTimeOffset.MaxValue)
        {
            return MaxSleep;
        }

        var wait = next - now;
        return wait < MinSleep ? MinSleep : wait > MaxSleep ? MaxSleep : wait;
    }
}
