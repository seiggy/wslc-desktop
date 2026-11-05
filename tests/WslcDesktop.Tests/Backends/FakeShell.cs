using WslcDesktop.Cli;

namespace WslcDesktop.Tests.Backends;

/// <summary>A scripted interactive shell: Emit pushes output as if the process wrote it; Sent records what was typed.</summary>
public sealed class FakeShell : IShellProcess
{
    private Action<string> _onOutput = _ => { };
    private Action<int> _onExit = _ => { };

    public List<string> Sent { get; } = [];

    public bool Disposed { get; private set; }

    public bool HasExited { get; private set; }

    public FakeShell Attach(Action<string> onOutput, Action<int> onExit)
    {
        _onOutput = onOutput;
        _onExit = onExit;
        return this;
    }

    public void Emit(string line) => _onOutput(line);

    public void Exit(int code)
    {
        HasExited = true;
        _onExit(code);
    }

    public Task SendLineAsync(string line)
    {
        Sent.Add(line);
        return Task.CompletedTask;
    }

    public void Dispose() => Disposed = true;
}
