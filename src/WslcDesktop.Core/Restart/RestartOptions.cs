namespace WslcDesktop.Restart;

public sealed class RestartOptions
{
    /// <summary>Consecutive restarts allowed before the engine declares a crash loop.</summary>
    public int MaxAttempts { get; init; } = 5;

    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>A container that stays up this long is healthy again and its attempt counter resets.</summary>
    public TimeSpan StableAfter { get; init; } = TimeSpan.FromSeconds(60);

    public TimeSpan StartGrace { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Snapshots a container must be missing from before its policy is forgotten.</summary>
    public int AbsentSnapshotsBeforeForget { get; init; } = 2;
}