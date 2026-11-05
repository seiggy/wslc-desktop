namespace WslcDesktop.Restart;

public sealed record RestartInfo(
    RestartPolicy Policy, RestartState State, int Attempts, int MaxAttempts, DateTimeOffset? NextAttemptAt, string? LastError)
{
    public static RestartInfo Empty { get; } = new(RestartPolicy.None, RestartState.Idle, 0, 0, null, null);
}