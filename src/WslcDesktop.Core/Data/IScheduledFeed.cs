namespace WslcDesktop.Data;

/// <summary>What the scheduler needs from a feed, independent of the data it holds.</summary>
public interface IScheduledFeed
{
    string Name { get; }

    bool HasValue { get; }

    string? LastError { get; }

    void MarkDirty();

    /// <summary>When the feed next wants a refresh; <see cref="DateTimeOffset.MaxValue"/> when it doesn't.</summary>
    DateTimeOffset NextDue(DateTimeOffset now, bool hidden);

    Task RefreshAsync(CancellationToken ct = default);

    /// <summary>Set by the scheduler so dirty marks and new watchers are noticed straight away.</summary>
    Action? Wake { get; set; }
}
