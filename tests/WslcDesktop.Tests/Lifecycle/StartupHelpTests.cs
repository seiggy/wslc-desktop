using WslcDesktop.Lifecycle.Startup;

namespace WslcDesktop.Tests.Lifecycle;

public class StartupHelpTests
{
    [Fact]
    public void Off_and_on_explain_what_start_with_windows_does()
    {
        var text = StartupHelp.Describe(new StartupStatus(StartupState.Off, ""), trayIconShown: true);

        Assert.StartsWith("Starts WSLc Desktop Manager when you sign in to Windows", text);
        Assert.Contains("Restart policies only work while the app is running", text);
    }

    [Fact]
    public void A_status_detail_replaces_the_explanation()
    {
        var text = StartupHelp.Describe(new StartupStatus(StartupState.DisabledByPolicy, "Blocked by your organization."), trayIconShown: false);

        Assert.Equal("Blocked by your organization.", text);
    }

    [Fact]
    public void When_on_without_a_tray_icon_it_suggests_minimize_to_tray()
    {
        var text = StartupHelp.Describe(new StartupStatus(StartupState.On, ""), trayIconShown: false);

        Assert.EndsWith("Turn on \"Minimize to tray\" to keep it out of the taskbar too.", text);
    }

    [Fact]
    public void When_on_with_a_tray_icon_there_is_no_suggestion()
    {
        var text = StartupHelp.Describe(new StartupStatus(StartupState.On, ""), trayIconShown: true);

        Assert.DoesNotContain("Minimize to tray", text);
    }

    [Theory]
    [InlineData(StartupState.DisabledByUser, true)]
    [InlineData(StartupState.Off, false)]
    [InlineData(StartupState.On, false)]
    [InlineData(StartupState.DisabledByPolicy, false)]
    public void Only_a_user_disable_points_to_windows_settings(StartupState state, bool expected) =>
        Assert.Equal(expected, new StartupStatus(state, "").NeedsWindowsSettings);
}
