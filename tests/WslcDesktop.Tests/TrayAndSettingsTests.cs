using WslcDesktop.Services;

namespace WslcDesktop.Tests;

public class AppSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wslcdesktop-tests-" + Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    [Fact]
    public void TrayPreferencesDefaultToOff()
    {
        var s = AppSettings.Load(SettingsPath);
        Assert.False(s.MinimizeToTray);
        Assert.False(s.CloseMinimizesToTray);
    }

    [Fact]
    public void TrayPreferencesRoundTrip()
    {
        new AppSettings { MinimizeToTray = true, CloseMinimizesToTray = true, CpuCount = 1 }.Save(SettingsPath);
        var s = AppSettings.Load(SettingsPath);
        Assert.True(s.MinimizeToTray);
        Assert.True(s.CloseMinimizesToTray);
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Fact]
    public void FileWithoutTrayKeysLoadsAsOff()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{\"CpuCount\":2,\"MemoryMb\":1024}");
        Assert.False(AppSettings.Load(SettingsPath).MinimizeToTray);
    }

    [Fact]
    public void CorruptFileFallsBackToDefaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{not json");
        Assert.False(AppSettings.Load(SettingsPath).MinimizeToTray);
    }
}

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
        Assert.Equal("WSLc Desktop", TrayPolicy.Tooltip(null, null));
        Assert.Equal("WSLc Desktop — 1 container running", TrayPolicy.Tooltip(1, 3));
        Assert.Equal("WSLc Desktop — 2 containers running", TrayPolicy.Tooltip(2, 3));
        Assert.Equal("2 of 3 containers running", TrayPolicy.StatusLine(2, 3));
        Assert.Equal("Status unavailable", TrayPolicy.StatusLine(null, null));
    }

    [Fact]
    public void HiddenWindowPollsSlower()
    {
        var visible = TimeSpan.FromSeconds(2);
        Assert.Equal(visible, TrayPolicy.PollInterval(false, visible));
        Assert.True(TrayPolicy.PollInterval(true, visible) > visible);
    }
}
