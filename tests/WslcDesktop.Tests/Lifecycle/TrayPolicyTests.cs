
using WslcDesktop.Lifecycle;
using WslcDesktop.Settings;

namespace WslcDesktop.Tests.Lifecycle;

public class TrayPolicyTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void TrayIconShownWhenEitherSettingIsOn(bool min, bool close, bool expected) =>
        Assert.Equal(expected, TrayPolicy.ShowTrayIcon(new AppSettings { MinimizeToTray = min, CloseMinimizesToTray = close }));

    [Fact]
    public void CloseIsInterceptedOnlyWhenEnabledAndNotExiting()
    {
        var on = new AppSettings { CloseMinimizesToTray = true };
        Assert.True(TrayPolicy.InterceptClose(on, exitRequested: false));
        Assert.False(TrayPolicy.InterceptClose(on, exitRequested: true));
        Assert.False(TrayPolicy.InterceptClose(new AppSettings(), exitRequested: false));
    }

    [Fact]
    public void MinimizeIndependentOfCloseSetting()
    {
        Assert.True(TrayPolicy.HideOnMinimize(new AppSettings { MinimizeToTray = true }));
        Assert.False(TrayPolicy.HideOnMinimize(new AppSettings { CloseMinimizesToTray = true }));
    }

    [Theory]
    [InlineData(16, 16)]
    [InlineData(17, 20)]
    [InlineData(24, 24)]
    [InlineData(28, 32)]
    [InlineData(48, 32)]
    public void PicksSmallestCoveringIconSize(int requested, int expected) =>
        Assert.Equal(expected, TrayPolicy.PickIconSize(requested));

    [Fact]
    public void IconFollowsTaskbarTheme()
    {
        Assert.Equal("tray-light-20.png", TrayPolicy.IconFileName(true, 20));
        Assert.Equal("tray-dark-32.png", TrayPolicy.IconFileName(false, 30));
    }

    [Fact]
    public void TooltipAndStatusText()
    {
        Assert.Equal("WSLc Desktop Manager", TrayPolicy.Tooltip(null, null));
        Assert.Equal("WSLc Desktop Manager — no containers running", TrayPolicy.Tooltip(0, 3));
        Assert.Equal("WSLc Desktop Manager — 1 container running", TrayPolicy.Tooltip(1, 3));
        Assert.Equal("WSLc Desktop Manager — 2 containers running", TrayPolicy.Tooltip(2, 3));
        Assert.Equal("2 of 3 containers running", TrayPolicy.StatusLine(2, 3));
        Assert.Equal("Status unavailable", TrayPolicy.StatusLine(null, null));
    }
}
