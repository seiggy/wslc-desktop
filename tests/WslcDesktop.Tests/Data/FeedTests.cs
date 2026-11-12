using WslcDesktop.Data;

namespace WslcDesktop.Tests.Data;

/// <summary>A clock tests move by hand; only <see cref="GetUtcNow"/> is used by feeds and the scheduler.</summary>
public sealed class ManualClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

public class FeedTests
{
    private static readonly FeedPolicy Policy = new(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60));

    private static Feed<string> Make(ManualClock clock, Func<CancellationToken, Task<string>> load, FeedPolicy? policy = null) =>
        new("test", load, () => policy ?? Policy, clock, string.Equals);

    [Fact]
    public async Task A_failed_read_keeps_the_last_value_and_reports_the_error()
    {
        var fail = false;
        var feed = Make(new ManualClock(), _ => fail ? throw new InvalidOperationException("denied") : Task.FromResult("v1"));
        await feed.RefreshAsync();

        fail = true;
        await feed.RefreshAsync();

        Assert.Equal("v1", feed.Latest);
        Assert.Equal("denied", feed.LastError);

        fail = false;
        await feed.RefreshAsync();
        Assert.Null(feed.LastError);
    }

    [Fact]
    public async Task Changed_fires_only_for_a_real_change_but_Loaded_fires_for_every_read()
    {
        var value = "a";
        var feed = Make(new ManualClock(), _ => Task.FromResult(new string(value.ToCharArray())));
        var (changed, loaded) = (0, 0);
        feed.Changed += () => changed++;
        feed.Loaded += () => loaded++;

        await feed.RefreshAsync();
        var first = feed.Latest;
        await feed.RefreshAsync();
        Assert.Same(first, feed.Latest);

        value = "b";
        await feed.RefreshAsync();

        Assert.Equal(2, changed);
        Assert.Equal(3, loaded);
    }

    [Fact]
    public async Task Requests_made_while_a_read_runs_collapse_into_one_more_read()
    {
        var calls = 0;
        var release = new TaskCompletionSource();
        var feed = Make(new ManualClock(), async _ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                await release.Task;
            }

            return "x";
        });

        var first = feed.RefreshAsync();
        var waiting = Enumerable.Range(0, 5).Select(_ => feed.RefreshAsync()).ToList();
        release.SetResult();
        await first;
        await Task.WhenAll(waiting);

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Due_times_follow_the_policy_for_watched_background_and_hidden()
    {
        var clock = new ManualClock();
        var feed = Make(clock, _ => Task.FromResult("x"));
        Assert.Equal(clock.Now, feed.NextDue(clock.Now, hidden: false));

        await feed.RefreshAsync();
        var read = clock.Now;
        Assert.Equal(read + TimeSpan.FromSeconds(10), feed.NextDue(clock.Now, hidden: false));
        Assert.Equal(read + TimeSpan.FromSeconds(60), feed.NextDue(clock.Now, hidden: true));

        using (feed.Watch())
        {
            Assert.Equal(read + TimeSpan.FromSeconds(2), feed.NextDue(clock.Now, hidden: false));
            // A page left open while the window is in the tray isn't being looked at.
            Assert.Equal(read + TimeSpan.FromSeconds(60), feed.NextDue(clock.Now, hidden: true));
        }

        Assert.Equal(read + TimeSpan.FromSeconds(10), feed.NextDue(clock.Now, hidden: false));
    }

    [Fact]
    public async Task A_dirty_feed_is_due_after_the_debounce_and_clean_again_once_read()
    {
        var clock = new ManualClock();
        var feed = Make(clock, _ => Task.FromResult("x"));
        await feed.RefreshAsync();

        clock.Advance(TimeSpan.FromSeconds(1));
        feed.MarkDirty();
        feed.MarkDirty();
        Assert.Equal(clock.Now + Feed<string>.Debounce, feed.NextDue(clock.Now, hidden: false));

        await feed.RefreshAsync();
        Assert.Equal(clock.Now + TimeSpan.FromSeconds(10), feed.NextDue(clock.Now, hidden: false));
    }

    [Fact]
    public async Task A_never_policy_only_reads_when_asked()
    {
        var clock = new ManualClock();
        var feed = Make(clock, _ => Task.FromResult("x"), FeedPolicy.Every(FeedPolicy.Never));
        await feed.RefreshAsync();

        Assert.Equal(DateTimeOffset.MaxValue, feed.NextDue(clock.Now, hidden: false));
    }

    [Fact]
    public void A_seeded_value_counts_as_a_read()
    {
        var clock = new ManualClock();
        var feed = Make(clock, _ => throw new InvalidOperationException("not called"));

        feed.Seed("seeded");

        Assert.Equal("seeded", feed.Latest);
        Assert.Equal(clock.Now + TimeSpan.FromSeconds(10), feed.NextDue(clock.Now, hidden: false));
    }

    [Fact]
    public async Task Marking_dirty_while_a_read_runs_asks_for_another_read()
    {
        var clock = new ManualClock();
        var release = new TaskCompletionSource();
        var feed = Make(clock, async _ =>
        {
            await release.Task;
            return "x";
        });

        var running = feed.RefreshAsync();
        // Invalidation comes from the UI thread and must never wait for the read.
        var marked = Task.Run(feed.MarkDirty);
        Assert.Same(marked, await Task.WhenAny(marked, Task.Delay(TimeSpan.FromSeconds(2))));
        release.SetResult();
        await running;

        Assert.Equal(clock.Now + Feed<string>.Debounce, feed.NextDue(clock.Now, hidden: false));
    }
}
