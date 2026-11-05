namespace WslcDesktop.Lifecycle.Startup;

public enum InstallForm
{
    /// <summary>Plain exe: the portable zip or a dev build.</summary>
    Unpackaged,

    /// <summary>Installed by Velopack: the exe lives in a versioned "current" folder that is replaced on update.</summary>
    Velopack,

    /// <summary>MSIX / Store: startup is a manifest StartupTask, not a registry entry.</summary>
    Msix,
}
