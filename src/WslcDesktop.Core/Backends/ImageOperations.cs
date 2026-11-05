using WslcDesktop.Images;
using WslcDesktop.Sessions;

namespace WslcDesktop.Backends;

public sealed class ImageOperations(BackendRouter router, IOperationEffects effects)
{
    public Task<IReadOnlyList<ImageSummary>> ListAsync(SessionHandle session) => router.For(session).ListImagesAsync();

    public async Task PullAsync(SessionHandle session, string uri, IProgress<PullProgress>? progress = null)
    {
        await router.For(session).PullImageAsync(uri, progress);
        effects.ImagesChanged(session.Name);
    }

    public async Task DeleteAsync(SessionHandle session, string target)
    {
        await router.For(session).DeleteImageAsync(target);
        effects.ImagesChanged(session.Name);
    }
}
