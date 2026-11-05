namespace WslcDesktop.Cli;

/// <summary>One wslc.exe run: arguments, optional extra environment variables, optional stdin (never logged; scrubbed from errors) and optional timeout.</summary>
public sealed record WslcInvocation(
    IReadOnlyList<string> Args,
    IReadOnlyDictionary<string, string>? Environment = null,
    string? StandardInput = null,
    TimeSpan? Timeout = null);
