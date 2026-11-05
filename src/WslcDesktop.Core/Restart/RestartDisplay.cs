namespace WslcDesktop.Restart;

/// <summary>User-facing wording for restart policies and their live status. UI-free so it can be tested.</summary>
public static class RestartDisplay
{
    public static readonly string[] ModeLabels = ["Never", "Always", "On failure", "Unless stopped"];

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
        RestartMode.Always => "Always",
        RestartMode.UnlessStopped => "Unless stopped",
        RestartMode.OnFailure => policy.MaxRetries > 0 ? $"On failure (max {policy.MaxRetries})" : "On failure",
        _ => "Never",
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
            RestartState.CrashLoop => "Crash loop",
            RestartState.Restarting => info.NextAttemptAt is { } at && at > now
                ? $"Restarting in {Math.Max(1, (int)Math.Ceiling((at - now).TotalSeconds))}s"
                : "Restarting…",
            _ => info.Attempts > 0 ? $"{Describe(info.Policy)} · restarted {info.Attempts}×" : Describe(info.Policy),
        };
    }

    /// <summary>One sentence for the detail page.</summary>
    public static string Long(RestartInfo info, DateTimeOffset now)
    {
        if (info.Policy.IsNone)
        {
            return "Not restarted automatically.";
        }

        var error = string.IsNullOrEmpty(info.LastError) ? string.Empty : $" Last restart failed: {info.LastError}";
        return info.State switch
        {
            RestartState.CrashLoop =>
                $"Crash loop: it kept exiting, so restarts stopped after {info.Attempts} attempts. Start it yourself to try again.{error}",
            RestartState.Restarting =>
                $"{Short(info, now)} (attempt {info.Attempts} of {info.MaxAttempts}).{error}",
            _ => info.Attempts > 0
                ? $"Running again after {info.Attempts} restart{(info.Attempts == 1 ? string.Empty : "s")}.{error}"
                : "Restarted automatically per policy while WSLc Desktop Manager is running.",
        };
    }
}
