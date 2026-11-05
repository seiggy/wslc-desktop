namespace WslcDesktop.Cli;

/// <summary>An interactive shell inside a container: lines go in with <see cref="SendLineAsync"/>, output and exit come back through callbacks given at start.</summary>
public interface IShellProcess : IDisposable
{
    bool HasExited { get; }

    Task SendLineAsync(string line);
}
