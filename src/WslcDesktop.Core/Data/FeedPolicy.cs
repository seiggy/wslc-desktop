namespace WslcDesktop.Data;

/// <summary>
/// How often a feed refreshes on its own: while a page shows it, while the window is open but nothing shows it, and
/// while the window is hidden in the tray. <see cref="Never"/> means only when it's marked dirty or asked for.
/// </summary>
public sealed record FeedPolicy(TimeSpan Watched, TimeSpan Background, TimeSpan Hidden)
{
    public static readonly TimeSpan Never = Timeout.InfiniteTimeSpan;

    public static FeedPolicy Every(TimeSpan interval) => new(interval, interval, interval);

    // A page left open while the window sits in the tray isn't being looked at, so hidden wins over watched.
    public TimeSpan IntervalFor(bool watched, bool hidden) => hidden ? Hidden : watched ? Watched : Background;
}
