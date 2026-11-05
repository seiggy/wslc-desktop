namespace WslcDesktop.Updates;

public static class UpdatePolicy
{
    public const string RepositoryUrl = "https://github.com/seiggy/wslc-desktop";

    /// <summary>Overrides the feed (a folder or URL) so updates can be tested without GitHub.</summary>
    public const string FeedOverrideVariable = "WSLC_UPDATE_FEED";

    public static int ClampProgress(int percent) => Math.Clamp(percent, 0, 100);

    /// <summary>Prerelease builds follow prereleases; stable builds stay on stable releases.</summary>
    public static bool IncludePrereleases(string? version) => version?.Contains('-') ?? false;

    public static UpdateView Describe(UpdateState state, string? availableVersion = null, int progress = 0, string? error = null) =>
        state switch
        {
            UpdateState.Unavailable => new(
                "Updates are managed elsewhere",
                "This copy wasn't installed with the WSLc Desktop Manager installer. Microsoft Store installs update through the Store; "
                    + "portable copies update by downloading a newer release.",
                false, false, false, false),
            UpdateState.Checking => new("Checking for updates…", "Looking for a newer release on GitHub.", false, false, false, true),
            UpdateState.UpToDate => new("You're up to date", "This is the latest release.", true, false, false, false),
            UpdateState.Available => new(
                $"Version {availableVersion} is available",
                "Download it now. Nothing changes until you restart.",
                true, true, false, false),
            UpdateState.Downloading => new(
                $"Downloading version {availableVersion}…",
                $"{ClampProgress(progress)}% complete.",
                false, false, false, true),
            UpdateState.ReadyToInstall => new(
                $"Version {availableVersion} is ready",
                "Restart to finish updating. Running containers keep running.",
                false, false, true, false),
            UpdateState.Error => new(
                "Couldn't check for updates",
                string.IsNullOrWhiteSpace(error) ? "Check your connection and try again." : error,
                true, false, false, false),
            _ => new("Check for updates", "See whether a newer release is available.", true, false, false, false),
        };
}
