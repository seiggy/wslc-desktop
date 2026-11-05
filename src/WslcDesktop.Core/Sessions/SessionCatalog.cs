namespace WslcDesktop.Sessions;

/// <summary>
/// Merges per-session lists (images, containers, volumes, networks) across every visible WSLc session. The app's own
/// session is read on every refresh; other sessions are throttled, and a session that fails keeps its last good rows
/// so the list doesn't flicker.
/// </summary>
public sealed class SessionCatalog<T>(
    string appSession,
    Func<Task<IReadOnlyList<string>>> listSessions,
    Func<string, Task<IReadOnlyList<T>>> listItems,
    TimeProvider time,
    TimeSpan otherSessionInterval)
{
    private readonly Dictionary<string, (IReadOnlyList<T> Items, DateTimeOffset At)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _cacheLock = new();
    private readonly Dictionary<string, int> _invalidatedAt = new(StringComparer.OrdinalIgnoreCase);
    private int _version;

    public SessionSnapshot<T>? Latest { get; private set; }

    public async Task<SessionSnapshot<T>> RefreshAsync(bool force = false)
    {
        await _gate.WaitAsync();
        try
        {
            var startedAt = Volatile.Read(ref _version);
            var failures = new List<SessionFailure>();
            IReadOnlyList<string> listed;
            try
            {
                listed = await listSessions();
            }
            catch (Exception e)
            {
                // Without the session list, still show the app's own session.
                listed = new[] { appSession };
                failures.Add(new SessionFailure("(session list)", e.Message));
            }

            var sessions = listed
                .Where(s => !string.Equals(s, appSession, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .Prepend(appSession)
                .ToList();

            lock (_cacheLock)
            {
                foreach (var gone in _cache.Keys.Where(k => !sessions.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList())
                {
                    _cache.Remove(gone);
                }
            }

            var now = time.GetUtcNow();
            var results = await Task.WhenAll(sessions.Select(async s =>
            {
                var isApp = string.Equals(s, appSession, StringComparison.OrdinalIgnoreCase);
                (IReadOnlyList<T> Items, DateTimeOffset At) hit;
                bool cached;
                lock (_cacheLock)
                {
                    cached = _cache.TryGetValue(s, out hit);
                }

                if (!force && !isApp && cached && now - hit.At < otherSessionInterval)
                {
                    return (Session: s, Items: (IReadOnlyList<T>?)hit.Items, Error: (string?)null, Fetched: false);
                }

                try
                {
                    return (s, await listItems(s), null, true);
                }
                catch (Exception e)
                {
                    return (s, null, e.Message, false);
                }
            }));

            var rows = new List<SessionEntry<T>>();
            foreach (var r in results)
            {
                if (r.Items is not null)
                {
                    if (r.Fetched)
                    {
                        lock (_cacheLock)
                        {
                            // Rows read before an Invalidate for this session may already be stale.
                            if (!_invalidatedAt.TryGetValue(r.Session, out var at) || at <= startedAt)
                            {
                                _cache[r.Session] = (r.Items, now);
                            }
                        }
                    }
                }
                else
                {
                    failures.Add(new SessionFailure(r.Session, r.Error ?? "Unknown error"));
                }

                IReadOnlyList<T> items;
                lock (_cacheLock)
                {
                    items = r.Items ?? (_cache.TryGetValue(r.Session, out var stale) ? stale.Items : []);
                }

                var isApp = string.Equals(r.Session, appSession, StringComparison.OrdinalIgnoreCase);
                rows.AddRange(items.Select(i => new SessionEntry<T>(r.Session, isApp, i)));
            }

            return Latest = new SessionSnapshot<T>(rows, sessions, failures);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Drops cached rows so the next refresh re-reads a session (after a change made through the app).</summary>
    public void Invalidate(string session)
    {
        // Called from the UI thread, so it must never wait for an in-flight refresh.
        lock (_cacheLock)
        {
            _cache.Remove(session);
            _invalidatedAt[session] = Interlocked.Increment(ref _version);
        }
    }
}
