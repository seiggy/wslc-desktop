namespace WslcDesktop.Cli;

/// <summary>The seam in front of wslc.exe: one-shot runs, streamed output and interactive shells.</summary>
public interface IWslcProcess
{
    /// <summary>Runs one invocation and returns stdout, throwing on a non-zero exit.</summary>
    Task<string> RunAsync(WslcInvocation invocation, CancellationToken ct = default);

    /// <summary>Streams stdout and stderr line by line until the process ends or <paramref name="ct"/> is cancelled.</summary>
    Task StreamAsync(WslcInvocation invocation, Action<string> onLine, CancellationToken ct = default);

    /// <summary>Starts a long-running process with piped stdin, reporting output lines and the exit code through callbacks.</summary>
    IShellProcess StartInteractive(WslcInvocation invocation, Action<string> onOutput, Action<int> onExit);
}
