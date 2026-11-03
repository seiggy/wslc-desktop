using Microsoft.UI.Xaml;

namespace WslcDesktop.Services;

/// <summary>An image as seen from one WSLc session. Image stores are per session, so the same image pulled in two sessions is two rows.</summary>
public sealed record SessionImage(string Session, bool IsThisApp, ImageSummary Image)
{
    /// <summary>Stable identity for in-place list updates. A multi-tag image appears once per tag, so the tag is part of the key.</summary>
    public string Key => $"{Session}|{Image.Id}|{Image.DisplayName}";

    public Visibility ThisAppVisibility => IsThisApp ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>True when "Run" can pull the image into the app's session: untagged images have no reference to pull.</summary>
    public bool CanCopyToApp => Image.IsTagged;
}

public sealed record SessionFailure(string Session, string Message);

public sealed record ImageSnapshot(
    IReadOnlyList<SessionImage> Images,
    IReadOnlyList<string> Sessions,
    IReadOnlyList<SessionFailure> Failures);

/// <summary>
/// Merges the image lists of every visible WSLc session. The app's own session is read on every refresh;
/// other sessions are throttled, and a session that fails keeps its last good rows so the list doesn't flicker.
/// </summary>
public sealed class ImageCatalog(
    string appSession,
    Func<Task<IReadOnlyList<string>>> listSessions,
    Func<string, Task<IReadOnlyList<ImageSummary>>> listImages,
    TimeProvider time,
    TimeSpan otherSessionInterval)
{
    private readonly Dictionary<string, (IReadOnlyList<ImageSummary> Images, DateTimeOffset At)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<ImageSnapshot> RefreshAsync(bool force = false)
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
                    return (Session: s, Images: (IReadOnlyList<ImageSummary>?)hit.Images, Error: (string?)null, Fetched: false);
                }

                try
                {
                    return (s, await listImages(s), null, true);
                }
                catch (Exception e)
                {
                    return (s, null, e.Message, false);
                }
            }));

            var rows = new List<SessionImage>();
            foreach (var r in results)
            {
                if (r.Images is not null)
                {
                    if (r.Fetched)
                    {
                        _cache[r.Session] = (r.Images, now);
                    }
                }
                else
                {
                    failures.Add(new SessionFailure(r.Session, r.Error ?? "Unknown error"));
                }

                var images = r.Images ?? (_cache.TryGetValue(r.Session, out var stale) ? stale.Images : []);
                var isApp = string.Equals(r.Session, appSession, StringComparison.OrdinalIgnoreCase);
                rows.AddRange(images.Select(i => new SessionImage(r.Session, isApp, i)));
            }

            return new ImageSnapshot(rows, sessions, failures);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Drops cached rows so the next refresh re-reads a session (after a pull or delete).</summary>
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
