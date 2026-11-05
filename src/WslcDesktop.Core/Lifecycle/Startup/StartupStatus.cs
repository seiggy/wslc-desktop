namespace WslcDesktop.Lifecycle.Startup;

public sealed record StartupStatus(StartupState State, string Detail)
{
    public bool IsOn => State == StartupState.On;

    public bool CanToggle => State is StartupState.On or StartupState.Off;

    /// <summary>Only the user can re-enable it, in Windows Settings, so the page links there.</summary>
    public bool NeedsWindowsSettings => State == StartupState.DisabledByUser;
}
