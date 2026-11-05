using WslcDesktop.Sessions;

namespace WslcDesktop.Images;

public sealed record ImageSnapshot(
    IReadOnlyList<SessionImage> Images,
    IReadOnlyList<string> Sessions,
    IReadOnlyList<SessionFailure> Failures);
