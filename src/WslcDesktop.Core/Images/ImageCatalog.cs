using WslcDesktop.Sessions;

namespace WslcDesktop.Images;

/// <summary>Images from every visible WSLc session; see <see cref="SessionCatalog{T}"/>.</summary>
public sealed class ImageCatalog(
    string appSession,
    Func<Task<IReadOnlyList<string>>> listSessions,
    Func<string, Task<IReadOnlyList<ImageSummary>>> listImages,
    TimeProvider time,
    TimeSpan otherSessionInterval)
{
    private readonly SessionCatalog<ImageSummary> _inner = new(appSession, listSessions, listImages, time, otherSessionInterval);

    public async Task<ImageSnapshot> RefreshAsync(bool force = false)
    {
        var snap = await _inner.RefreshAsync(force);
        return new ImageSnapshot(
            snap.Items.Select(e => new SessionImage(e.Session, e.IsThisApp, e.Item)).ToList(),
            snap.Sessions,
            snap.Failures);
    }

    public void Invalidate(string session) => _inner.Invalidate(session);
}
