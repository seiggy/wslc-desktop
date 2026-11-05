using System.Globalization;
using System.Text.RegularExpressions;

namespace WslcDesktop.Restart;

/// <summary>An app-level restart policy: no, always, on-failure[:max], unless-stopped.</summary>
public readonly record struct RestartPolicy(RestartMode Mode, int MaxRetries = 0)
{
    public static RestartPolicy None => new(RestartMode.No);

    public bool IsNone => Mode == RestartMode.No;

    public static bool TryParse(string? text, out RestartPolicy policy)
    {
        policy = None;
        var t = (text ?? string.Empty).Trim().Trim('"', '\'').ToLowerInvariant();
        switch (t)
        {
            case "" or "no" or "false":
                return true;
            case "always":
                policy = new RestartPolicy(RestartMode.Always);
                return true;
            case "unless-stopped":
                policy = new RestartPolicy(RestartMode.UnlessStopped);
                return true;
            case "on-failure":
                policy = new RestartPolicy(RestartMode.OnFailure);
                return true;
        }

        var m = Regex.Match(t, @"^on-failure:(\d+)$");
        if (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var max))
        {
            policy = new RestartPolicy(RestartMode.OnFailure, max);
            return true;
        }

        return false;
    }

    public static RestartPolicy Parse(string? text) => TryParse(text, out var p) ? p : None;

    // The same spelling `docker run --restart` and compose use.
    public override string ToString() => Mode switch
    {
        RestartMode.Always => "always",
        RestartMode.UnlessStopped => "unless-stopped",
        RestartMode.OnFailure => MaxRetries > 0 ? $"on-failure:{MaxRetries}" : "on-failure",
        _ => "no",
    };
}