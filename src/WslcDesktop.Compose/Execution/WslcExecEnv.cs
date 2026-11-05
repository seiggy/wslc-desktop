namespace WslcDesktop.Compose.Execution;

/// <summary>Like <see cref="WslcExec"/>, but also sets variables in the wslc process's environment (used to pass secrets).</summary>
public delegate Task<string> WslcExecEnv(IReadOnlyList<string> args, IReadOnlyDictionary<string, string> environment);
