using WslcDesktop.Containers;
using WslcDesktop.Images;
using WslcDesktop.Resources;
using WslcDesktop.Sessions;

namespace WslcDesktop.Data;

/// <summary>Where the store reads each kind of data from (wslc.exe or the SDK, decided by the host).</summary>
public sealed record StoreSources(
    Func<CancellationToken, Task<IReadOnlyList<SessionRow>>> ListSessions,
    Func<string, CancellationToken, Task<IReadOnlyList<ContainerRow>>> ListContainers,
    Func<string, CancellationToken, Task<IReadOnlyList<ImageSummary>>> ListImages,
    Func<string, ResourceKind, CancellationToken, Task<IReadOnlyList<ResourceRow>>> ListResources,
    Func<string, IReadOnlyList<string>, CancellationToken, Task<IReadOnlyDictionary<string, string>>> InspectContainers,
    Func<CancellationToken, Task<VersionInfo>> Versions,
    Func<string, IReadOnlyList<string>?, CancellationToken, Task<IReadOnlyDictionary<string, StatsSample>>> Stats);

public sealed record VersionInfo(string Wsl, string Kernel, string SessionManager);

/// <summary>
/// Refresh intervals. With a live event stream a session's changes arrive as events, so polling is only a safety net;
/// without one (or before it connects) the store falls back to polling. Images emit no events, so they always poll.
/// </summary>
public sealed record StorePolicies
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    public FeedPolicy Sessions { get; init; } = new(S(10), S(30), S(120));

    public FeedPolicy Versions { get; init; } = FeedPolicy.Every(TimeSpan.FromMinutes(10));

    /// <summary>The app session's containers drive restart policies and the tray, so they're read even while hidden.</summary>
    public FeedPolicy AppContainersWithEvents { get; init; } = new(S(30), S(30), S(120));

    /// <summary>An owned session also gets SDK callbacks (crashes, init exits, our own operations).</summary>
    public FeedPolicy AppContainersWithSdk { get; init; } = new(S(10), S(10), S(30));

    public FeedPolicy AppContainersPolling { get; init; } = new(S(2), S(2), S(10));

    public FeedPolicy ContainersWithEvents { get; init; } = new(S(30), S(120), FeedPolicy.Never);

    public FeedPolicy ContainersPolling { get; init; } = new(S(5), S(30), FeedPolicy.Never);

    public FeedPolicy InspectWithEvents { get; init; } = new(S(60), S(300), FeedPolicy.Never);

    public FeedPolicy InspectPolling { get; init; } = new(S(15), S(120), FeedPolicy.Never);

    public FeedPolicy ResourcesWithEvents { get; init; } = new(S(60), S(300), FeedPolicy.Never);

    public FeedPolicy ResourcesPolling { get; init; } = new(S(15), S(120), FeedPolicy.Never);

    public FeedPolicy Images { get; init; } = new(S(15), S(120), FeedPolicy.Never);

    /// <summary>Stats for the CPU and memory cells of the container list.</summary>
    public TimeSpan StatsList { get; init; } = S(5);

    /// <summary>Stats while a container's charts are open.</summary>
    public TimeSpan StatsCharts { get; init; } = S(2);

    /// <summary>Background reads (each usually a wslc.exe launch) allowed at once.</summary>
    public int MaxConcurrentReads { get; init; } = 3;
}
