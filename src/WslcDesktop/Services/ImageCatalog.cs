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

public sealed record ImageSnapshot(
    IReadOnlyList<SessionImage> Images,
    IReadOnlyList<string> Sessions,
    IReadOnlyList<SessionFailure> Failures);

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
