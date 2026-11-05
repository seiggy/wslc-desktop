namespace WslcDesktop.Compose.Execution;

/// <summary>Runs wslc with the given arguments (without --session) and returns stdout; throws on a non-zero exit.</summary>
public delegate Task<string> WslcExec(IReadOnlyList<string> args);
