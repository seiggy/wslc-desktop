namespace WslcDesktop.Sessions;

public sealed record SessionSnapshot<T>(
    IReadOnlyList<SessionEntry<T>> Items,
    IReadOnlyList<string> Sessions,
    IReadOnlyList<SessionFailure> Failures);
