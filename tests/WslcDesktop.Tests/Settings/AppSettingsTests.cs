
using WslcDesktop.Settings;

namespace WslcDesktop.Tests.Settings;

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

    [Fact]
    public void AppearanceDefaultsFollowWindows()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{\"CpuCount\":1,\"MinimizeToTray\":true}");
        var settings = AppSettings.Load(SettingsPath);
        Assert.Equal("System", settings.Theme);
        Assert.Null(settings.AccentColor);
        Assert.True(settings.MinimizeToTray);
    }

    [Theory]
    [InlineData("System", null)]
    [InlineData("Light", "#127c10")]
    [InlineData("Dark", "#F08000")]
    public void AppearanceRoundTrips(string theme, string? accent)
    {
        new AppSettings { Theme = theme, AccentColor = accent, CpuCount = 1 }.Save(SettingsPath);
        var settings = AppSettings.Load(SettingsPath);
        Assert.Equal(theme, settings.Theme);
        Assert.Equal(accent?.ToUpperInvariant(), settings.AccentColor);
    }

    [Theory]
    [InlineData("#12345")]
    [InlineData("#00112233")]
    [InlineData("#GG0000")]
    [InlineData("red")]
    [InlineData("")]
    public void InvalidAppearanceDoesNotDiscardOtherPreferences(string accent)
    {
        new AppSettings { Theme = "Unknown", AccentColor = accent, CpuCount = 1, MinimizeToTray = true }.Save(SettingsPath);
        var settings = AppSettings.Load(SettingsPath);
        Assert.Equal("System", settings.Theme);
        Assert.Null(settings.AccentColor);
        Assert.True(settings.MinimizeToTray);
    }

    [Fact]
    public void CustomAccentShadesRemainReadableAcrossBothThemes()
    {
        for (uint r = 0; r <= 255; r += 17)
        {
            for (uint g = 0; g <= 255; g += 17)
            {
                for (uint b = 0; b <= 255; b += 17)
                {
                    var rgb = (r << 16) | (g << 8) | b;
                    var palette = AccentPalette.Create(rgb);
                    Assert.Equal(rgb, palette[0]);
                    foreach (var shade in palette[1..4])
                    {
                        Assert.True(AccentPalette.Contrast(shade, 0xFFFFFF) >= 5.5);
                        Assert.True(AccentPalette.Contrast(shade, 0xF3F3F3) >= 4.5);
                    }

                    foreach (var shade in palette[4..7])
                    {
                        Assert.True(AccentPalette.Contrast(shade, 0x202020) >= 5.5);
                        Assert.True(AccentPalette.Contrast(shade, 0) >= 7);
                    }
                }
            }
        }
    }
}
