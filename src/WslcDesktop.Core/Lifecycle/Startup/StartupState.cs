namespace WslcDesktop.Lifecycle.Startup;

public enum StartupState
{
    Off,
    On,

    /// <summary>The user turned it off in Windows (Settings or Task Manager); only they can turn it back on.</summary>
    DisabledByUser,

    DisabledByPolicy,
    Unavailable,
}
