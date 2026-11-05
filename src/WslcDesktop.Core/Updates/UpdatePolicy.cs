using WslcDesktop.Localization;
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
                UiStrings.Get("UpdatesAreManagedElsewhere"),
                UiStrings.Get("ThisCopyWasnTInstalledWithTheWSLcDesktop"),
                false, false, false, false),
            UpdateState.Checking => new(UiStrings.Get("CheckingForUpdates"), UiStrings.Get("LookingForANewerReleaseOnGitHub"), false, false, false, true),
            UpdateState.UpToDate => new(UiStrings.Get("YouReUpToDate"), UiStrings.Get("ThisIsTheLatestRelease"), true, false, false, false),
            UpdateState.Available => new(
                UiStrings.Format("VersionIsAvailable", availableVersion),
                UiStrings.Get("DownloadItNowNothingChangesUntilYouRestart"),
                true, true, false, false),
            UpdateState.Downloading => new(
                UiStrings.Format("DownloadingVersion", availableVersion),
                UiStrings.Format("Complete", ClampProgress(progress)),
                false, false, false, true),
            UpdateState.ReadyToInstall => new(
                UiStrings.Format("VersionIsReady", availableVersion),
                UiStrings.Get("RestartToFinishUpdatingRunningContainersKeepRunning"),
                false, false, true, false),
            UpdateState.Error => new(
                UiStrings.Get("CouldnTCheckForUpdates"),
                string.IsNullOrWhiteSpace(error) ? UiStrings.Get("CheckYourConnectionAndTryAgain") : error,
                true, false, false, false),
            _ => new(UiStrings.Get("CheckForUpdates"), UiStrings.Get("SeeWhetherANewerReleaseIsAvailable"), true, false, false, false),
        };
}
