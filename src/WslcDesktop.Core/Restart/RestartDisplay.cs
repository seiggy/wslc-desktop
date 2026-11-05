using WslcDesktop.Localization;
namespace WslcDesktop.Restart;

/// <summary>User-facing wording for restart policies and their live status. UI-free so it can be tested.</summary>
public static class RestartDisplay
{
    public static string[] ModeLabels => [UiStrings.Get("Never"), UiStrings.Get("Always"), UiStrings.Get("OnFailure"), UiStrings.Get("UnlessStopped")];

    public static int ModeIndex(RestartPolicy policy) => policy.Mode switch
    {
        RestartMode.Always => 1,
        RestartMode.OnFailure => 2,
        RestartMode.UnlessStopped => 3,
        _ => 0,
    };

    public static RestartPolicy FromSelection(int modeIndex, int maxRetries) => modeIndex switch
    {
        1 => new RestartPolicy(RestartMode.Always),
        2 => new RestartPolicy(RestartMode.OnFailure, Math.Max(0, maxRetries)),
        3 => new RestartPolicy(RestartMode.UnlessStopped),
        _ => RestartPolicy.None,
    };

    public static string Describe(RestartPolicy policy) => policy.Mode switch
    {
        RestartMode.Always => UiStrings.Get("Always"),
        RestartMode.UnlessStopped => UiStrings.Get("UnlessStopped"),
        RestartMode.OnFailure => policy.MaxRetries > 0 ? UiStrings.Format("OnFailureMax", policy.MaxRetries) : UiStrings.Get("OnFailure"),
        _ => UiStrings.Get("Never"),
    };

    /// <summary>Short text for the containers list; empty when the container has no policy.</summary>
    public static string Short(RestartInfo info, DateTimeOffset now)
    {
        if (info.Policy.IsNone)
        {
            return string.Empty;
        }

        return info.State switch
        {
            RestartState.CrashLoop => UiStrings.Get("CrashLoop"),
            RestartState.Restarting => info.NextAttemptAt is { } at && at > now
                ? UiStrings.Format("RestartingInS", Math.Max(1, (int)Math.Ceiling((at - now).TotalSeconds)))
                : UiStrings.Get("Restarting"),
            _ => info.Attempts > 0 ? UiStrings.Format("Restarted", Describe(info.Policy), info.Attempts) : Describe(info.Policy),
        };
    }

    /// <summary>One sentence for the detail page.</summary>
    public static string Long(RestartInfo info, DateTimeOffset now)
    {
        if (info.Policy.IsNone)
        {
            return UiStrings.Get("NotRestartedAutomatically");
        }

        var error = string.IsNullOrEmpty(info.LastError) ? string.Empty : UiStrings.Format("LastRestartFailed", info.LastError);
        return info.State switch
        {
            RestartState.CrashLoop =>
                UiStrings.Format("CrashLoopItKeptExitingSoRestartsStoppedAfter", info.Attempts, error),
            RestartState.Restarting =>
                UiStrings.Format("AttemptOf", Short(info, now), info.Attempts, info.MaxAttempts, error),
            _ => info.Attempts > 0
                ? UiStrings.Format(info.Attempts == 1 ? "RestartedOnce" : "RestartedMultiple", info.Attempts, error)
                : UiStrings.Get("RestartedAutomaticallyPerPolicyWhileWSLcDesktopManagerIs"),
        };
    }
}
