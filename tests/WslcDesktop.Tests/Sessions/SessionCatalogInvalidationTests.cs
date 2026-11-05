using WslcDesktop.Sessions;

namespace WslcDesktop.Tests.Sessions;

public class SessionCatalogInvalidationTests
{
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch;
    }

    [Fact]
    public async Task Invalidate_returns_immediately_while_a_refresh_is_in_flight()
    {
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var catalog = new SessionCatalog<string>(
            "app",
            () => Task.FromResult<IReadOnlyList<string>>(["app", "lab"]),
            async s =>
            {
                started.TrySetResult();
                await release.Task;
                return [s];
            },
            new Clock(),
            TimeSpan.FromSeconds(10));

        var refresh = catalog.RefreshAsync();
        await started.Task;

        // The UI thread calls Invalidate synchronously; it must not wait for the refresh.
        var invalidate = Task.Run(() => catalog.Invalidate("lab"));
        var finished = await Task.WhenAny(invalidate, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.Same(invalidate, finished);
        release.SetResult();
        await refresh;
    }

    [Fact]
    public async Task A_refresh_in_flight_during_Invalidate_does_not_cache_its_stale_rows()
    {
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var calls = 0;
        var catalog = new SessionCatalog<string>(
            "app",
            () => Task.FromResult<IReadOnlyList<string>>(["app", "lab"]),
            async s =>
            {
                if (s == "lab" && Interlocked.Increment(ref calls) == 1)
                {
                    started.TrySetResult();
                    await release.Task;
                }

                return [s];
            },
            new Clock(),
            TimeSpan.FromSeconds(10));

        var refresh = catalog.RefreshAsync();
        await started.Task;
        catalog.Invalidate("lab");
        release.SetResult();
        await refresh;

        await catalog.RefreshAsync();

        Assert.Equal(2, calls);
    }
}
