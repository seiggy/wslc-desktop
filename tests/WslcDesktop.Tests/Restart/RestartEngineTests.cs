
using WslcDesktop.Restart;
using WslcDesktop.Restart.Monitoring;
using WslcDesktop.Restart.Persistence;
using System.Diagnostics.CodeAnalysis;

namespace WslcDesktop.Tests.Restart;

public class RestartEngineTests
{
    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class MemoryStore : IRestartStore
    {
        public Dictionary<string, RestartEntry> Data { get; } = [];

        public int SnapshotCalls { get; private set; }

        public IReadOnlyDictionary<string, RestartEntry> Snapshot()
        {
            SnapshotCalls++;
            return new Dictionary<string, RestartEntry>(Data);
        }

        public bool TryGet(string name, [NotNullWhen(true)] out RestartEntry? entry) => Data.TryGetValue(name, out entry);

        public void Set(string name, RestartEntry? entry)
        {
            if (entry is null) Data.Remove(name); else Data[name] = entry;
        }
    }

    private sealed class FakeActions : IRestartActions
    {
        public List<string> Started { get; } = [];

        public Exception? Fail { get; set; }

        public Task StartAsync(string name, CancellationToken ct)
        {
            if (Fail is not null) throw Fail;
            Started.Add(name);
            return Task.CompletedTask;
        }
    }

    private readonly FakeClock _clock = new();
    private readonly MemoryStore _store = new();
    private readonly FakeActions _actions = new();

    private RestartEngine Engine(RestartOptions? o = null) => new(_store, _actions, _clock, o);

    private static ContainerObservation Exited(string name = "c", int? code = 1) => new(name, "exited", code);

    private static ContainerObservation Running(string name = "c") => new(name, "running", null);

    private void Policy(string text, bool userStopped = false, string name = "c") =>
        _store.Set(name, new RestartEntry(text, userStopped));

    [Fact]
    public async Task AlwaysRestartsOnFirstSnapshotWithoutDelay()
    {
        Policy("always");
        await Engine().ProcessAsync([Exited(code: 0)]);
        Assert.Equal(["c"], _actions.Started);
    }

    [Fact]
    public async Task A_restart_asks_for_a_snapshot_when_its_start_grace_ends()
    {
        Policy("always");
        var e = Engine();
        await e.ProcessAsync([Exited()]);
        Assert.Equal(_clock.GetUtcNow() + new RestartOptions().StartGrace, e.NextCheck);

        // It crashes again inside the grace: ignored for now, but the check is still wanted.
        _clock.Advance(TimeSpan.FromSeconds(1));
        await e.ProcessAsync([Exited()]);
        Assert.Single(_actions.Started);
        Assert.NotNull(e.NextCheck);

        // The check at the end of the grace sees the crash and schedules the next attempt.
        _clock.Advance(new RestartOptions().StartGrace);
        await e.ProcessAsync([Exited()]);
        Assert.NotNull(e.NextPending);
        Assert.Equal(e.NextPending, e.NextCheck);
    }

    [Fact]
    public async Task A_failed_start_asks_for_a_retry_check()
    {
        Policy("always");
        _actions.Fail = new InvalidOperationException("image gone");
        var e = Engine();

        await e.ProcessAsync([Exited()]);

        Assert.Null(e.NextPending);
        Assert.Equal(_clock.GetUtcNow() + new RestartOptions().BaseDelay, e.NextCheck);
    }

    [Fact]
    public async Task A_running_container_needs_no_check()
    {
        Policy("always");
        var e = Engine();
        await e.ProcessAsync([Exited()]);

        await e.ProcessAsync([Running()]);

        Assert.Null(e.NextCheck);
    }

    [Fact]
    public async Task NoPolicyNeverRestarts()
    {
        var e = Engine();
        await e.ProcessAsync([Exited()]);
        Assert.Empty(_actions.Started);
    }

    [Fact]
    public async Task OnFailureIgnoresCleanExitButRestartsFailures()
    {
        Policy("on-failure");
        var e = Engine();
        await e.ProcessAsync([Exited(code: 0)]);
        Assert.Empty(_actions.Started);
        await e.ProcessAsync([Exited(code: 137)]);
        _clock.Advance(TimeSpan.FromSeconds(1));
        await e.ProcessAsync([Exited(code: 137)]);
        Assert.Single(_actions.Started);
    }

    [Fact]
    public async Task OnFailureTreatsUnknownExitCodeAsFailure()
    {
        Policy("on-failure");
        await Engine().ProcessAsync([Exited(code: null)]);
        Assert.Single(_actions.Started);
    }

    [Fact]
    public async Task OnFailureHonoursMaxRetries()
    {
        Policy("on-failure:2");
        var e = Engine();
        await e.ProcessAsync([Exited()]);
        for (var i = 0; i < 6; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(30));
            await e.ProcessAsync([Exited()]);
        }

        Assert.Equal(2, _actions.Started.Count);
        Assert.Equal(RestartState.CrashLoop, e.GetInfo("c").State);
    }

    [Fact]
    public async Task UserStoppedContainerIsNotRestarted()
    {
        Policy("always");
        var e = Engine();
        await e.ProcessAsync([Running()]);
        e.NotifyUserStopped("c");
        _clock.Advance(TimeSpan.FromSeconds(10));
        await e.ProcessAsync([Exited(code: 0)]);
        Assert.Empty(_actions.Started);
    }

    [Fact]
    public async Task UserStartAfterStopResumesSupervision()
    {
        Policy("always");
        var e = Engine();
        await e.ProcessAsync([Running()]);
        e.NotifyUserStopped("c");
        e.NotifyUserStarted("c");
        _clock.Advance(TimeSpan.FromSeconds(10));
        await e.ProcessAsync([Exited()]);
        _clock.Advance(TimeSpan.FromSeconds(10));
        await e.ProcessAsync([Exited()]);
        Assert.Single(_actions.Started);
    }

    [Fact]
    public async Task StopFromEarlierRunSticksOnlyForUnlessStopped()
    {
        Policy("always", userStopped: true);
        Policy("unless-stopped", userStopped: true, name: "u");
        await Engine().ProcessAsync([Exited("c"), Exited("u")]);
        Assert.Equal(["c"], _actions.Started);
    }

    [Fact]
    public async Task SettingPolicyOnStoppedContainerDoesNotStartIt()
    {
        var e = Engine();
        await e.ProcessAsync([Running("other")]);
        e.SetPolicy("c", new RestartPolicy(RestartMode.Always), currentlyRunning: false);
        await e.ProcessAsync([Exited(code: 0)]);
        Assert.Empty(_actions.Started);
    }

    [Fact]
    public async Task BackoffDelaysLaterRestartsAndDoubles()
    {
        Policy("always");
        var e = Engine();
        await e.ProcessAsync([Running()]);
        await e.ProcessAsync([Exited()]);
        Assert.Empty(_actions.Started);
        Assert.Equal(RestartState.Restarting, e.GetInfo("c").State);

        _clock.Advance(TimeSpan.FromSeconds(1));
        await e.ProcessAsync([Exited()]);
        Assert.Single(_actions.Started);

        _clock.Advance(TimeSpan.FromSeconds(5));
        await e.ProcessAsync([Exited()]);
        _clock.Advance(TimeSpan.FromSeconds(1));
        await e.ProcessAsync([Exited()]);
        Assert.Single(_actions.Started);
        _clock.Advance(TimeSpan.FromSeconds(1));
        await e.ProcessAsync([Exited()]);
        Assert.Equal(2, _actions.Started.Count);
    }

    [Fact]
    public void BackoffIsCapped()
    {
        var e = Engine();
        Assert.Equal(TimeSpan.FromSeconds(1), e.Backoff(1));
        Assert.Equal(TimeSpan.FromSeconds(8), e.Backoff(4));
        Assert.Equal(TimeSpan.FromSeconds(60), e.Backoff(20));
    }

    [Fact]
    public void PolicyLookupDoesNotCopyTheStore()
    {
        Policy("always");
        var e = Engine();
        var snapshots = _store.SnapshotCalls;

        Assert.Equal(new RestartPolicy(RestartMode.Always), e.GetPolicy("c"));
        Assert.Equal(snapshots, _store.SnapshotCalls);
    }

    [Fact]
    public async Task CrashLoopStopsRestartingAndIsVisible()
    {
        Policy("always");
        var e = Engine(new RestartOptions { MaxAttempts = 3 });
        await e.ProcessAsync([Exited()]);
        for (var i = 0; i < 10; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(30));
            await e.ProcessAsync([Exited()]);
        }

        Assert.Equal(3, _actions.Started.Count);
        Assert.Equal(RestartState.CrashLoop, e.GetInfo("c").State);
        Assert.Equal("Crash loop", RestartDisplay.Short(e.GetInfo("c"), _clock.GetUtcNow()));
    }

    [Fact]
    public async Task StableRunResetsAttemptsAndCrashLoop()
    {
        Policy("always");
        var e = Engine(new RestartOptions { MaxAttempts = 1 });
        await e.ProcessAsync([Exited()]);
        _clock.Advance(TimeSpan.FromSeconds(30));
        await e.ProcessAsync([Exited()]);
        Assert.Equal(RestartState.CrashLoop, e.GetInfo("c").State);

        await e.ProcessAsync([Running()]);
        _clock.Advance(TimeSpan.FromSeconds(61));
        await e.ProcessAsync([Running()]);
        Assert.Equal(RestartState.Idle, e.GetInfo("c").State);
        Assert.Equal(0, e.GetInfo("c").Attempts);
    }

    [Fact]
    public async Task SnapshotRightAfterOurStartIsIgnored()
    {
        Policy("always");
        var e = Engine();
        await e.ProcessAsync([Exited()]);
        _clock.Advance(TimeSpan.FromSeconds(1));
        await e.ProcessAsync([Exited()]);
        Assert.Single(_actions.Started);
        Assert.Equal(1, e.GetInfo("c").Attempts);
    }

    [Fact]
    public async Task FailedStartIsReportedAndRetried()
    {
        Policy("always");
        var e = Engine();
        _actions.Fail = new InvalidOperationException("boom");
        await e.ProcessAsync([Exited()]);
        Assert.Equal("boom", e.GetInfo("c").LastError);

        _actions.Fail = null;
        await e.ProcessAsync([Exited()]);
        _clock.Advance(TimeSpan.FromSeconds(5));
        await e.ProcessAsync([Exited()]);
        Assert.Single(_actions.Started);
    }

    [Fact]
    public async Task CreatedPausedAndRestartingContainersAreNotCrashes()
    {
        Policy("always");
        var e = Engine();
        await e.ProcessAsync([new("c", "created", null)]);
        await e.ProcessAsync([new("c", "paused", null)]);
        await e.ProcessAsync([new("c", "restarting", null)]);
        Assert.Empty(_actions.Started);
    }

    [Fact]
    public async Task PolicyIsForgottenOnlyAfterRepeatedAbsence()
    {
        Policy("always");
        var e = Engine();
        await e.ProcessAsync([Running("other")]);
        Assert.True(_store.Data.ContainsKey("c"));
        await e.ProcessAsync([Running("other")]);
        Assert.False(_store.Data.ContainsKey("c"));
    }

    [Fact]
    public void RemovingContainerDropsPolicy()
    {
        Policy("always");
        Engine().NotifyRemoved("c");
        Assert.Empty(_store.Data);
    }

    [Theory]
    [InlineData("always", RestartMode.Always, 0)]
    [InlineData("unless-stopped", RestartMode.UnlessStopped, 0)]
    [InlineData("on-failure", RestartMode.OnFailure, 0)]
    [InlineData("on-failure:3", RestartMode.OnFailure, 3)]
    [InlineData("no", RestartMode.No, 0)]
    [InlineData("", RestartMode.No, 0)]
    public void ParsesPolicies(string text, RestartMode mode, int max)
    {
        Assert.True(RestartPolicy.TryParse(text, out var p));
        Assert.Equal(new RestartPolicy(mode, max), p);
        Assert.Equal(p, RestartPolicy.Parse(p.ToString()));
    }

    [Theory]
    [InlineData("sometimes")]
    [InlineData("on-failure:x")]
    public void RejectsUnknownPolicies(string text) => Assert.False(RestartPolicy.TryParse(text, out _));

    [Theory]
    [InlineData("Exited (1) 4 seconds ago", 1)]
    [InlineData("Exited (137) 2 minutes ago", 137)]
    [InlineData("Up 3 minutes", null)]
    [InlineData(null, null)]
    public void ParsesExitCodeFromStatus(string? status, int? expected) =>
        Assert.Equal(expected, ContainerStatusText.ExitCode(status));

    [Fact]
    public void StorePersistsAcrossInstances()
    {
        var path = Path.Combine(Path.GetTempPath(), "wslc-rp-" + Guid.NewGuid().ToString("N"), "p.json");
        try
        {
            new RestartPolicyStore(path).Set("c", new RestartEntry("always", true));
            var loaded = new RestartPolicyStore(path).Snapshot();
            Assert.Equal(new RestartEntry("always", true), loaded["c"]);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }
}
