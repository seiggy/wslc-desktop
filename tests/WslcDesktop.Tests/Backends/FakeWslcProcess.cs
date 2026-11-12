using WslcDesktop.Cli;

namespace WslcDesktop.Tests.Backends;

/// <summary>Records every wslc invocation and replays canned output; the test adapter for the wslc.exe seam.</summary>
public sealed class FakeWslcProcess : IWslcProcess
{
    private readonly Queue<object> _outputs = new();
    private readonly List<string> _streamLines = [];

    public List<WslcInvocation> Calls { get; } = [];

    public Exception? FailWith { get; set; }

    public FakeShell Shell { get; } = new();

    public FakeWslcProcess Returns(string output)
    {
        _outputs.Enqueue(output);
        return this;
    }

    /// <summary>Fails just the next unanswered call, the way a non-zero wslc exit does.</summary>
    public FakeWslcProcess Throws(Exception error)
    {
        _outputs.Enqueue(error);
        return this;
    }

    public FakeWslcProcess StreamsLines(params string[] lines)
    {
        _streamLines.AddRange(lines);
        return this;
    }

    public IReadOnlyList<string> LastArgs => Calls[^1].Args;

    public Task<string> RunAsync(WslcInvocation invocation, CancellationToken ct = default)
    {
        Calls.Add(invocation);
        if (FailWith is not null)
        {
            throw FailWith;
        }

        var next = _outputs.Count > 0 ? _outputs.Dequeue() : string.Empty;
        return next is Exception queued ? Task.FromException<string>(queued) : Task.FromResult((string)next);
    }

    public Task StreamAsync(WslcInvocation invocation, Action<string> onLine, CancellationToken ct = default)
    {
        Calls.Add(invocation);
        foreach (var line in _streamLines)
        {
            onLine(line);
        }

        return Task.CompletedTask;
    }

    public IShellProcess StartInteractive(WslcInvocation invocation, Action<string> onOutput, Action<int> onExit)
    {
        Calls.Add(invocation);
        return Shell.Attach(onOutput, onExit);
    }
}
