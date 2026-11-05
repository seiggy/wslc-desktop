using WslcDesktop.Localization;
using System.Diagnostics;
using WslcDesktop.Lifecycle;

namespace WslcDesktop.Cli;

/// <summary>An interactive shell running through <c>wslc exec -i</c> with piped stdin/stdout/stderr.</summary>
public sealed class ShellProcess : IShellProcess
{
    private readonly Process _process;
    private bool _disposed;

    public ShellProcess(ProcessStartInfo info, Action<string> onOutput, Action<int> onExit)
    {
        try
        {
            _process = ChildProcessTracker.Start(info);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException(UiStrings.Get("WslcExeWasnTFoundOnPATH"));
        }

        _process.StandardInput.AutoFlush = true;
        _ = Pump(_process.StandardOutput, onOutput);
        _ = Pump(_process.StandardError, onOutput);
        _ = Task.Run(async () =>
        {
            await _process.WaitForExitAsync();
            onExit(_process.ExitCode);
        });
    }

    public bool HasExited => _process.HasExited;

    public async Task SendLineAsync(string line)
    {
        if (_process.HasExited)
        {
            return;
        }

        try
        {
            await _process.StandardInput.WriteAsync(line + "\n");
            await _process.StandardInput.FlushAsync();
        }
        catch (IOException)
        {
            // The shell ended between the check and the write.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }

        _process.Dispose();
    }

    private static async Task Pump(StreamReader reader, Action<string> onOutput)
    {
        try
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                onOutput(line);
            }
        }
        catch (ObjectDisposedException)
        {
            // Shell was closed by the user.
        }
    }
}
