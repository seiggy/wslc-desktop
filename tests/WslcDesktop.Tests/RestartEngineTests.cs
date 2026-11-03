using WslcDesktop.Services;

namespace WslcDesktop.Tests;

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

        public IReadOnlyDictionary<string, RestartEntry> Snapshot() => new Dictionary<string, RestartEntry>(Data);

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

public class StartupPolicyTests
{
    [Theory]
    [InlineData("--background", true)]
    [InlineData("--MINIMIZED", true)]
    [InlineData("--other", false)]
    public void DetectsBackgroundLaunch(string arg, bool expected) =>
        Assert.Equal(expected, StartupPolicy.IsBackgroundLaunch([arg]));

    [Fact]
    public void VelopackUsesStableStub()
    {
        var cmd = StartupPolicy.RunCommand(InstallForm.Velopack, @"C:\App\current\App.exe", @"C:\My App\App.exe");
        Assert.Equal("\"C:\\My App\\App.exe\" --background", cmd);
    }

    [Fact]
    public void UnpackagedUsesProcessPath()
    {
        var cmd = StartupPolicy.RunCommand(InstallForm.Unpackaged, @"C:\App\App.exe", null);
        Assert.Equal(@"C:\App\App.exe --background", cmd);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(new byte[] { 2, 0, 0 }, true)]
    [InlineData(new byte[] { 6, 0, 0 }, true)]
    [InlineData(new byte[] { 3, 0, 0 }, false)]
    [InlineData(new byte[] { 7, 0, 0 }, false)]
    public void ReadsStartupApproved(byte[]? data, bool enabled) =>
        Assert.Equal(enabled, StartupPolicy.IsApprovedByUser(data));

    [Fact]
    public void ComparesExecutablesIgnoringArgsAndQuotes() =>
        Assert.True(StartupPolicy.SameExecutable("\"C:\\a b\\x.exe\" --background", "c:\\a b\\X.EXE"));

    [Fact]
    public void BackgroundLaunchHidesOnlyWithTray()
    {
        var s = AppSettings.Load(Path.Combine(Path.GetTempPath(), "none-" + Guid.NewGuid().ToString("N") + ".json"));
        Assert.Equal(BackgroundStart.Normal, StartupPolicy.Resolve(false, s));
        Assert.Equal(BackgroundStart.MinimizedToTaskbar, StartupPolicy.Resolve(true, s));
        s.CloseMinimizesToTray = true;
        Assert.Equal(BackgroundStart.HiddenInTray, StartupPolicy.Resolve(true, s));
    }
}
