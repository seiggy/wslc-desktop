
using WslcDesktop.Images;

namespace WslcDesktop.Tests.Images;

public class ImageCatalogTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static ImageSummary Img(string repo, string id = "sha256:aaaaaaaaaaaaaaaa") =>
        new(repo, "latest", id, string.Empty, "1MB", "1 day ago", 0);

    private static (ImageCatalog Catalog, Clock Clock, Dictionary<string, int> Calls, Dictionary<string, Func<IReadOnlyList<ImageSummary>>> Sources) Make(params string[] sessions)
    {
        var clock = new Clock();
        var calls = new Dictionary<string, int>();
        var sources = new Dictionary<string, Func<IReadOnlyList<ImageSummary>>>();
        var catalog = new ImageCatalog(
            "WslcDesktop",
            () => Task.FromResult<IReadOnlyList<string>>(sessions),
            s =>
            {
                calls[s] = calls.GetValueOrDefault(s) + 1;
                return Task.FromResult(sources[s]());
            },
            clock,
            TimeSpan.FromSeconds(10));
        return (catalog, clock, calls, sources);
    }

    [Fact]
    public async Task Merges_sessions_with_app_session_first_and_owner_flag()
    {
        var (catalog, _, _, sources) = Make("wslc-cli-me", "WslcDesktop", "alpha");
        sources["WslcDesktop"] = () => [Img("app")];
        sources["wslc-cli-me"] = () => [Img("cli")];
        sources["alpha"] = () => [Img("a")];

        var snap = await catalog.RefreshAsync();

        Assert.Equal(["WslcDesktop", "alpha", "wslc-cli-me"], snap.Sessions);
        Assert.Equal(["WslcDesktop", "alpha", "wslc-cli-me"], snap.Images.Select(i => i.Session));
        Assert.True(snap.Images[0].IsThisApp);
        Assert.All(snap.Images.Skip(1), i => Assert.False(i.IsThisApp));
        Assert.Empty(snap.Failures);
    }

    [Fact]
    public async Task Same_image_in_two_sessions_has_distinct_keys()
    {
        var (catalog, _, _, sources) = Make("WslcDesktop", "other");
        sources["WslcDesktop"] = () => [Img("alpine")];
        sources["other"] = () => [Img("alpine")];

        var snap = await catalog.RefreshAsync();

        Assert.Equal(2, snap.Images.Select(i => i.Key).Distinct().Count());
    }

    [Fact]
    public async Task Failing_session_keeps_last_rows_and_reports_failure()
    {
        var (catalog, clock, _, sources) = Make("WslcDesktop", "other");
        sources["WslcDesktop"] = () => [Img("app")];
        sources["other"] = () => [Img("cli")];
        await catalog.RefreshAsync();

        clock.Now += TimeSpan.FromSeconds(30);
        sources["other"] = () => throw new InvalidOperationException("denied");
        var snap = await catalog.RefreshAsync();

        Assert.Contains(snap.Images, i => i.Session == "other");
        var failure = Assert.Single(snap.Failures);
        Assert.Equal("other", failure.Session);
        Assert.Equal("denied", failure.Message);
    }

    [Fact]
    public async Task Failing_session_with_no_cache_is_skipped()
    {
        var (catalog, _, _, sources) = Make("WslcDesktop", "bad");
        sources["WslcDesktop"] = () => [Img("app")];
        sources["bad"] = () => throw new InvalidOperationException("nope");

        var snap = await catalog.RefreshAsync();

        Assert.Single(snap.Images);
        Assert.Single(snap.Failures);
    }

    [Fact]
    public async Task Other_sessions_are_throttled_but_app_session_is_not()
    {
        var (catalog, clock, calls, sources) = Make("WslcDesktop", "other");
        sources["WslcDesktop"] = () => [Img("app")];
        sources["other"] = () => [Img("cli")];

        await catalog.RefreshAsync();
        clock.Now += TimeSpan.FromSeconds(3);
        await catalog.RefreshAsync();
        Assert.Equal(2, calls["WslcDesktop"]);
        Assert.Equal(1, calls["other"]);

        clock.Now += TimeSpan.FromSeconds(10);
        await catalog.RefreshAsync();
        Assert.Equal(2, calls["other"]);

        await catalog.RefreshAsync(force: true);
        Assert.Equal(3, calls["other"]);
    }

    [Fact]
    public async Task Invalidate_forces_a_reread()
    {
        var (catalog, _, calls, sources) = Make("WslcDesktop", "other");
        sources["WslcDesktop"] = () => [];
        sources["other"] = () => [];
        await catalog.RefreshAsync();
        catalog.Invalidate("other");
        await catalog.RefreshAsync();

        Assert.Equal(2, calls["other"]);
    }

    [Fact]
    public async Task Vanished_session_drops_its_rows()
    {
        var clock = new Clock();
        IReadOnlyList<string> listed = ["WslcDesktop", "other"];
        var catalog = new ImageCatalog("WslcDesktop", () => Task.FromResult(listed), _ => Task.FromResult<IReadOnlyList<ImageSummary>>([Img("x")]), clock, TimeSpan.FromSeconds(10));
        Assert.Equal(2, (await catalog.RefreshAsync()).Images.Count);

        listed = ["WslcDesktop"];
        var snap = await catalog.RefreshAsync();

        Assert.Single(snap.Images);
    }

    [Fact]
    public async Task Session_list_failure_falls_back_to_app_session()
    {
        var catalog = new ImageCatalog(
            "WslcDesktop",
            () => throw new InvalidOperationException("no wslc"),
            _ => Task.FromResult<IReadOnlyList<ImageSummary>>([Img("x")]),
            TimeProvider.System,
            TimeSpan.FromSeconds(10));

        var snap = await catalog.RefreshAsync();

        Assert.Single(snap.Images);
        Assert.True(snap.Images[0].IsThisApp);
        Assert.Single(snap.Failures);
    }

    [Fact]
    public void Untagged_images_cannot_be_copied_to_the_app_session()
    {
        var untagged = new SessionImage("other", false, new ImageSummary("<none>", "<none>", "sha256:abc", "", "1MB", "", 0));
        Assert.False(untagged.CanCopyToApp);
        Assert.True(new SessionImage("other", false, Img("alpine")).CanCopyToApp);
    }
}
