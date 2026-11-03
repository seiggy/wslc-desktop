namespace WslcDesktop.Services;

/// <summary>One item as seen from the WSLc session that owns it. Stores are per session, so the same name in two sessions is two entries.</summary>
public sealed record SessionEntry<T>(string Session, bool IsThisApp, T Item);

public sealed record SessionFailure(string Session, string Message);

public sealed record SessionSnapshot<T>(
    IReadOnlyList<SessionEntry<T>> Items,
    IReadOnlyList<string> Sessions,
    IReadOnlyList<SessionFailure> Failures);

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

    public SessionSnapshot<T>? Latest { get; private set; }

    public async Task<SessionSnapshot<T>> RefreshAsync(bool force = false)
    {
        await _gate.WaitAsync();
        try
        {
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

            foreach (var gone in _cache.Keys.Where(k => !sessions.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList())
            {
                _cache.Remove(gone);
            }

            var now = time.GetUtcNow();
            var results = await Task.WhenAll(sessions.Select(async s =>
            {
                var isApp = string.Equals(s, appSession, StringComparison.OrdinalIgnoreCase);
                if (!force && !isApp && _cache.TryGetValue(s, out var hit) && now - hit.At < otherSessionInterval)
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
                        _cache[r.Session] = (r.Items, now);
                    }
                }
                else
                {
                    failures.Add(new SessionFailure(r.Session, r.Error ?? "Unknown error"));
                }

                var items = r.Items ?? (_cache.TryGetValue(r.Session, out var stale) ? stale.Items : []);
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
        _gate.Wait();
        try
        {
            _cache.Remove(session);
        }
        finally
        {
            _gate.Release();
        }
    }
}
