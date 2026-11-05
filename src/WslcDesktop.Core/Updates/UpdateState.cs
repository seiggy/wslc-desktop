namespace WslcDesktop.Updates;

public enum UpdateState
{
    /// <summary>Not installed by Velopack (portable zip, MSIX, or dev build): updates are managed elsewhere.</summary>
    Unavailable,
    Idle,
    Checking,
    UpToDate,
    Available,
    Downloading,
    ReadyToInstall,
    Error,
}
