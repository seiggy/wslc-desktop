namespace WslcDesktop.Restart;

/// <summary>What the engine knows about one container's restart handling, for display.</summary>
public enum RestartState
{
    /// <summary>No policy, or nothing pending.</summary>
    Idle,

    /// <summary>The container exited and a restart is scheduled.</summary>
    Restarting,

    /// <summary>Restarts kept failing; the engine stopped trying.</summary>
    CrashLoop,
}