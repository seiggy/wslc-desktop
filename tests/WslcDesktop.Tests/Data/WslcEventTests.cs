using WslcDesktop.Cli;
using WslcDesktop.Data;
using WslcDesktop.Tests.Backends;

namespace WslcDesktop.Tests.Data;

public class WslcEventTests
{
    [Fact]
    public void Container_events_carry_type_action_and_name()
    {
        var e = WslcEvent.Parse("""{"Action":"start","Actor":{"ID":"382141a73f3a","Attributes":{"name":"probec","image":"alpine"}},"Type":"container"}""");

        Assert.Equal(new WslcEvent("container", "start", "382141a73f3a", "probec"), e);
    }

    [Fact]
    public void Exec_actions_drop_the_command_suffix()
    {
        var e = WslcEvent.Parse("""{"Action":"exec_start: sh ","Actor":{"ID":"a","Attributes":{"name":"web"}},"Type":"container"}""");

        Assert.Equal("exec_start", e!.Action);
    }

    [Fact]
    public void Volume_events_fall_back_to_the_id_for_the_name()
    {
        var e = WslcEvent.Parse("""{"Action":"create","Actor":{"ID":"probevol","Attributes":{}},"Type":"volume"}""");

        Assert.Equal("probevol", e!.Name);
    }

    [Theory]
    [InlineData("Operation aborted ")]
    [InlineData("Error code: E_ABORT")]
    [InlineData("")]
    [InlineData("{not json")]
    [InlineData("""{"Action":"start"}""")]
    public void Anything_else_on_the_stream_is_ignored(string line) => Assert.Null(WslcEvent.Parse(line));
}

public class SessionEventStreamsTests
{
    /// <summary>Emits lines, then stays connected until cancelled (like a healthy <c>wslc events</c>).</summary>
    private sealed class LiveStream(params string[] lines) : IWslcProcess
    {
        public List<IReadOnlyList<string>> Invocations { get; } = [];

        public Task<string> RunAsync(WslcInvocation invocation, CancellationToken ct = default) => Task.FromResult(string.Empty);

        public async Task StreamAsync(WslcInvocation invocation, Action<string> onLine, CancellationToken ct = default)
        {
            lock (Invocations)
            {
                Invocations.Add(invocation.Args);
            }

            foreach (var line in lines)
            {
                onLine(line);
            }

            await Task.Delay(Timeout.Infinite, ct);
        }

        public IShellProcess StartInteractive(WslcInvocation invocation, Action<string> onOutput, Action<int> onExit) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Each_session_gets_a_stream_and_its_events()
    {
        var process = new LiveStream("""{"Action":"die","Actor":{"ID":"1","Attributes":{"name":"web"}},"Type":"container"}""");
        var events = new List<(string, WslcEvent)>();
        var states = new List<(string, StreamState)>();
        using var streams = new SessionEventStreams(
            process,
            (s, e) => { lock (events) { events.Add((s, e)); } },
            (s, st) => { lock (states) { states.Add((s, st)); } });

        streams.Sync(["app", "lab"]);
        await WaitUntilAsync(() => { lock (events) { return events.Count == 2; } });

        Assert.Equal(["app", "lab"], events.Select(e => e.Item1).Order());
        Assert.All(events, e => Assert.Equal("die", e.Item2.Action));
        Assert.Contains(("app", StreamState.Connected), states);
        Assert.True(streams.IsLive("lab"));
        lock (process.Invocations)
        {
            Assert.Contains(process.Invocations, a => a.SequenceEqual(["--session", "app", "events", "--format", "json"]));
        }

        streams.Sync(["app"]);
        await WaitUntilAsync(() => !streams.IsLive("lab"));
        Assert.True(streams.IsLive("app"));
    }

    [Fact]
    public async Task A_stream_that_ends_straight_away_reports_a_failure()
    {
        var process = new FakeWslcProcess().StreamsLines("Operation aborted", "Error code: E_ABORT");
        var states = new List<StreamState>();
        using var streams = new SessionEventStreams(process, (_, _) => { }, (_, st) => { lock (states) { states.Add(st); } });

        streams.Sync(["gone"]);
        await WaitUntilAsync(() => { lock (states) { return states.Contains(StreamState.Failed); } });

        Assert.Equal(StreamState.Connected, states[0]);
        Assert.False(streams.IsLive("gone"));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out.");
            await Task.Delay(10);
        }
    }
}
