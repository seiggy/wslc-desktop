using System.Diagnostics;

namespace WslcDesktop.Services;

/// <summary>An interactive shell running through <c>wslc exec -i</c> with piped stdin/stdout/stderr.</summary>
public sealed class ShellProcess : IDisposable
{
    private readonly Process _process;
    private bool _disposed;

    internal ShellProcess(ProcessStartInfo info, Action<string> onOutput, Action<int> onExit)
    {
        try
        {
            _process = Process.Start(info) ?? throw new InvalidOperationException("Couldn't start wslc.exe.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("wslc.exe wasn't found on PATH.");
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
            await _process.StandardInput.WriteLineAsync(line);
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
