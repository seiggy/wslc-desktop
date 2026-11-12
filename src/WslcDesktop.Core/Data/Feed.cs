namespace WslcDesktop.Data;

/// <summary>
/// One dataset kept current in the background. Readers take <see cref="Latest"/> without waiting; it's never thrown
/// away, only replaced by a newer read, so a page always has something to show. Marking a feed dirty schedules a
/// refresh (debounced) and keeps the old value until the new one arrives.
/// </summary>
public sealed class Feed<T> : IScheduledFeed where T : class
{
    /// <summary>Bursts of dirty marks (several events for one change) collapse into one read.</summary>
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(250);

    private readonly Func<CancellationToken, Task<T>> _load;
    private readonly Func<FeedPolicy> _policy;
    private readonly TimeProvider _time;
    private readonly Func<T, T, bool> _same;
    private readonly Func<bool>? _sharedWatch;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _lock = new();
    private long _requested;
    private long _completed;
    private int _watchers;
    private bool _dirty;
    private DateTimeOffset _dirtyAt;
    private DateTimeOffset? _lastAttempt;
    private T? _latest;
    private string? _error;
    private DateTimeOffset? _loadedAt;

    /// <param name="same">Value equality, so <see cref="Changed"/> only fires for a real change; defaults to reference equality.</param>
    /// <param name="sharedWatch">Also counts as watched when this is true (for a page watching every session of a kind).</param>
    public Feed(
        string name,
        Func<CancellationToken, Task<T>> load,
        Func<FeedPolicy> policy,
        TimeProvider time,
        Func<T, T, bool>? same = null,
        Func<bool>? sharedWatch = null)
    {
        Name = name;
        _load = load;
        _policy = policy;
        _time = time;
        _same = same ?? ((a, b) => ReferenceEquals(a, b));
        _sharedWatch = sharedWatch;
    }

    public string Name { get; }

    /// <summary>The last good value, or null before the first successful read.</summary>
    public T? Latest => Volatile.Read(ref _latest);

    public bool HasValue => Latest is not null;

    /// <summary>Why the last read failed, or null after a success. <see cref="Latest"/> keeps the last good value.</summary>
    public string? LastError
    {
        get
        {
            lock (_lock)
            {
                return _error;
            }
        }
    }

    public DateTimeOffset? LoadedAt
    {
        get
        {
            lock (_lock)
            {
                return _loadedAt;
            }
        }
    }

    public bool IsWatched => Volatile.Read(ref _watchers) > 0 || _sharedWatch?.Invoke() == true;

    public Action? Wake { get; set; }

    /// <summary>Raised (on any thread) when <see cref="Latest"/> or <see cref="LastError"/> changes.</summary>
    public event Action? Changed;

    /// <summary>Raised (on any thread) after every successful read, changed or not.</summary>
    public event Action? Loaded;

    /// <summary>Asks for a fresh read soon; the current value stays until it arrives.</summary>
    public void MarkDirty()
    {
        lock (_lock)
        {
            if (!_dirty)
            {
                _dirty = true;
                _dirtyAt = _time.GetUtcNow();
            }
        }

        Wake?.Invoke();
    }

    /// <summary>Uses the faster watched interval until the returned handle is disposed.</summary>
    public IDisposable Watch()
    {
        Interlocked.Increment(ref _watchers);
        Wake?.Invoke();
        return new ReleaseHandle(() => Interlocked.Decrement(ref _watchers));
    }

    /// <summary>Takes a value read elsewhere (for example at startup) as if this feed had read it.</summary>
    public void Seed(T value)
    {
        lock (_lock)
        {
            _latest = value;
            _error = null;
            _lastAttempt = _loadedAt = _time.GetUtcNow();
        }

        Changed?.Invoke();
    }

    public DateTimeOffset NextDue(DateTimeOffset now, bool hidden)
    {
        DateTimeOffset last;
        lock (_lock)
        {
            if (_dirty)
            {
                return _dirtyAt + Debounce;
            }

            if (_lastAttempt is not { } attempted)
            {
                return now;
            }

            last = attempted;
        }

        var interval = _policy().IntervalFor(IsWatched, hidden);
        return interval == FeedPolicy.Never || interval < TimeSpan.Zero ? DateTimeOffset.MaxValue : last + interval;
    }

    /// <summary>
    /// Reads now, or joins a read that started after this call was made. Concurrent callers share one read, and a
    /// call made while a read is running gets a second read afterwards, so the result is never older than the call.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var wanted = Interlocked.Increment(ref _requested);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (Interlocked.Read(ref _completed) >= wanted)
            {
                return;
            }

            var generation = Interlocked.Read(ref _requested);
            lock (_lock)
            {
                _dirty = false;
            }

            await LoadAsync(ct).ConfigureAwait(false);
            Interlocked.Exchange(ref _completed, generation);
        }
        finally
        {
            _gate.Release();
            Wake?.Invoke();
        }
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        T value;
        try
        {
            value = await _load(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            bool errorChanged;
            lock (_lock)
            {
                _lastAttempt = _time.GetUtcNow();
                errorChanged = _error != e.Message;
                _error = e.Message;
            }

            if (errorChanged)
            {
                Changed?.Invoke();
            }

            return;
        }

        bool changed;
        lock (_lock)
        {
            _lastAttempt = _loadedAt = _time.GetUtcNow();
            var same = _latest is not null && _same(_latest, value);
            changed = !same || _error is not null;
            if (!same)
            {
                // An equal value keeps the old instance, so readers can compare references.
                _latest = value;
            }

            _error = null;
        }

        if (changed)
        {
            Changed?.Invoke();
        }

        Loaded?.Invoke();
    }
}
