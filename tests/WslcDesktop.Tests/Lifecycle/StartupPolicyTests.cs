
using WslcDesktop.Lifecycle.Startup;
using WslcDesktop.Settings;

namespace WslcDesktop.Tests.Lifecycle;

public class StartupPolicyTests
{
    [Theory]
    [InlineData("--background", true)]
    [InlineData("--MINIMIZED", true)]
    [InlineData("--other", false)]
    public void DetectsBackgroundLaunch(string arg, bool expected) =>
        Assert.Equal(expected, StartupPolicy.IsBackgroundLaunch([arg]));

    [Fact]
    public void VelopackUsesStableStub()
    {
        var cmd = StartupPolicy.RunCommand(InstallForm.Velopack, @"C:\App\current\App.exe", @"C:\My App\App.exe");
        Assert.Equal("\"C:\\My App\\App.exe\" --background", cmd);
    }

    [Fact]
    public void UnpackagedUsesProcessPath()
    {
        var cmd = StartupPolicy.RunCommand(InstallForm.Unpackaged, @"C:\App\App.exe", null);
        Assert.Equal(@"C:\App\App.exe --background", cmd);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(new byte[] { 2, 0, 0 }, true)]
    [InlineData(new byte[] { 6, 0, 0 }, true)]
    [InlineData(new byte[] { 3, 0, 0 }, false)]
    [InlineData(new byte[] { 7, 0, 0 }, false)]
    public void ReadsStartupApproved(byte[]? data, bool enabled) =>
        Assert.Equal(enabled, StartupPolicy.IsApprovedByUser(data));

    [Fact]
    public void ComparesExecutablesIgnoringArgsAndQuotes() =>
        Assert.True(StartupPolicy.SameExecutable("\"C:\\a b\\x.exe\" --background", "c:\\a b\\X.EXE"));

    [Fact]
    public void BackgroundLaunchHidesOnlyWithTray()
    {
        var s = AppSettings.Load(Path.Combine(Path.GetTempPath(), "none-" + Guid.NewGuid().ToString("N") + ".json"));
        Assert.Equal(BackgroundStart.Normal, StartupPolicy.Resolve(false, s));
        Assert.Equal(BackgroundStart.MinimizedToTaskbar, StartupPolicy.Resolve(true, s));
        s.CloseMinimizesToTray = true;
        Assert.Equal(BackgroundStart.HiddenInTray, StartupPolicy.Resolve(true, s));
    }
}
