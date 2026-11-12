using WslcDesktop.Data;

namespace WslcDesktop.Tests.Data;

public class FeedSchedulerTests
{
    private static Feed<string> Feed(ManualClock clock, Func<Task<string>> load, FeedPolicy? policy = null) =>
        new("f", _ => load(), () => policy ?? FeedPolicy.Every(TimeSpan.FromSeconds(10)), clock, string.Equals);

    [Fact]
    public async Task Only_due_feeds_are_read()
    {
        var clock = new ManualClock();
        var scheduler = new FeedScheduler(clock);
        var (a, b) = (0, 0);
        var fa = Feed(clock, () => Task.FromResult($"{++a}"));
        var fb = Feed(clock, () => Task.FromResult($"{++b}"), FeedPolicy.Every(TimeSpan.FromSeconds(30)));
        scheduler.Add(fa);
        scheduler.Add(fb);

        await scheduler.RunDueAsync();
        Assert.Equal((1, 1), (a, b));

        clock.Advance(TimeSpan.FromSeconds(11));
        await scheduler.RunDueAsync();
        Assert.Equal((2, 1), (a, b));

        fb.MarkDirty();
        clock.Advance(Feed<string>.Debounce);
        await scheduler.RunDueAsync();
        Assert.Equal((2, 2), (a, b));
    }

    [Fact]
    public async Task Reads_are_limited_to_the_concurrency_budget()
    {
        var clock = new ManualClock();
        var scheduler = new FeedScheduler(clock, maxConcurrent: 1);
        var inFlight = 0;
        var peak = 0;
        var release = new TaskCompletionSource();
        async Task<string> Load()
        {
            peak = Math.Max(peak, Interlocked.Increment(ref inFlight));
            await release.Task;
            Interlocked.Decrement(ref inFlight);
            return "x";
        }

        scheduler.Add(Feed(clock, Load));
        scheduler.Add(Feed(clock, Load));
        var run = scheduler.RunDueAsync();
        await Task.Delay(100);
        release.SetResult();
        await run;

        Assert.Equal(1, peak);
    }

    [Fact]
    public async Task A_feed_already_being_read_is_not_started_twice()
    {
        var clock = new ManualClock();
        var scheduler = new FeedScheduler(clock);
        var calls = 0;
        var release = new TaskCompletionSource();
        scheduler.Add(Feed(clock, async () =>
        {
            Interlocked.Increment(ref calls);
            await release.Task;
            return "x";
        }));

        var first = scheduler.RunDueAsync();
        var second = scheduler.RunDueAsync();
        release.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Hidden_uses_the_hidden_interval()
    {
        var clock = new ManualClock();
        var scheduler = new FeedScheduler(clock) { Hidden = true };
        var calls = 0;
        scheduler.Add(Feed(clock, () => Task.FromResult($"{++calls}"), new FeedPolicy(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), FeedPolicy.Never)));

        await scheduler.RunDueAsync();
        clock.Advance(TimeSpan.FromMinutes(5));
        await scheduler.RunDueAsync();
        Assert.Equal(1, calls);

        scheduler.Hidden = false;
        await scheduler.RunDueAsync();
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task A_failing_feed_does_not_stop_the_others()
    {
        var clock = new ManualClock();
        var scheduler = new FeedScheduler(clock);
        var good = Feed(clock, () => Task.FromResult("ok"));
        scheduler.Add(Feed(clock, () => throw new InvalidOperationException("boom")));
        scheduler.Add(good);

        await scheduler.RunDueAsync();

        Assert.Equal("ok", good.Latest);
    }
}
