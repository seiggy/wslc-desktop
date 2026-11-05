
using WslcDesktop.Updates;

namespace WslcDesktop.Tests.Updates;

public class UpdatePolicyTests
{
    [Theory]
    [InlineData("0.1.0-preview", true)]
    [InlineData("1.2.3", false)]
    [InlineData(null, false)]
    public void Prereleases_follow_the_installed_channel(string? version, bool expected) =>
        Assert.Equal(expected, UpdatePolicy.IncludePrereleases(version));

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(42, 42)]
    [InlineData(200, 100)]
    public void Progress_is_clamped(int input, int expected) => Assert.Equal(expected, UpdatePolicy.ClampProgress(input));

    [Fact]
    public void Unavailable_offers_no_actions()
    {
        var v = UpdatePolicy.Describe(UpdateState.Unavailable);
        Assert.False(v.CanCheck || v.CanDownload || v.CanRestart);
    }

    [Fact]
    public void Available_offers_download_with_version()
    {
        var v = UpdatePolicy.Describe(UpdateState.Available, "1.2.0");
        Assert.True(v.CanDownload);
        Assert.Contains("1.2.0", v.Title);
    }

    [Fact]
    public void Downloading_shows_progress_and_blocks_actions()
    {
        var v = UpdatePolicy.Describe(UpdateState.Downloading, "1.2.0", 250);
        Assert.True(v.ShowProgress);
        Assert.Contains("100%", v.Message);
        Assert.False(v.CanCheck || v.CanDownload);
    }

    [Fact]
    public void Ready_offers_restart_and_error_allows_retry()
    {
        Assert.True(UpdatePolicy.Describe(UpdateState.ReadyToInstall, "1.2.0").CanRestart);
        var err = UpdatePolicy.Describe(UpdateState.Error, error: "offline");
        Assert.True(err.CanCheck);
        Assert.Equal("offline", err.Message);
    }
}
