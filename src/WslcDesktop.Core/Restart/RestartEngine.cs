using WslcDesktop.Restart.Monitoring;
using WslcDesktop.Restart.Persistence;

namespace WslcDesktop.Restart;

/// <summary>
/// The app-level restart policy. Driven by container snapshots (from the monitor), so it behaves the same
/// whether this app owns the WSLc session or is attached to one. Pure logic: time and actions are injected.
/// </summary>
public sealed class RestartEngine(IRestartStore store, IRestartActions actions, TimeProvider clock, RestartOptions? options = null)
{
    private readonly RestartOptions _o = options ?? new RestartOptions();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _lock = new();
    private readonly Dictionary<string, Track> _tracks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _absent = new(StringComparer.Ordinal);
    private bool _initialized;

    public event Action? Changed;

    public RestartPolicy GetPolicy(string name) =>
        store.TryGet(name, out var e) ? RestartPolicy.Parse(e.Policy) : RestartPolicy.None;

    public RestartInfo GetInfo(string name)
    {
        var policy = GetPolicy(name);
        if (policy.IsNone)
        {
            return RestartInfo.Empty;
        }

        lock (_lock)
        {
            if (!_tracks.TryGetValue(name, out var t))
            {
                return new RestartInfo(policy, RestartState.Idle, 0, Limit(policy), null, null);
            }

            var state = t.GaveUp && t.RunningSince is null ? RestartState.CrashLoop : t.Pending is not null ? RestartState.Restarting : RestartState.Idle;
            return new RestartInfo(policy, state, t.Attempts, Limit(policy), t.Pending, t.LastError);
        }
    }

    /// <summary>Applies a policy chosen by the user. A container that is down right now stays down.</summary>
    public void SetPolicy(string name, RestartPolicy policy, bool currentlyRunning)
    {
        if (policy.IsNone)
        {
            store.Set(name, null);
        }
        else
        {
            store.Set(name, new RestartEntry(policy.ToString(), !currentlyRunning));
        }

        lock (_lock)
        {
            _tracks.Remove(name);
        }

        Changed?.Invoke();
    }

    /// <summary>The user stopped the container from this app: never restart it because of that stop.</summary>
    public void NotifyUserStopped(string name)
    {
        if (store.TryGet(name, out var e) && !e.UserStopped)
        {
            store.Set(name, e with { UserStopped = true });
        }

        lock (_lock)
        {
            _tracks.Remove(name);
        }

        Changed?.Invoke();
    }

    /// <summary>The user started the container: a fresh run, with attempts and crash-loop state cleared.</summary>
    public void NotifyUserStarted(string name)
    {
        if (store.TryGet(name, out var e) && e.UserStopped)
        {
            store.Set(name, e with { UserStopped = false });
        }

        lock (_lock)
        {
            _tracks.Remove(name);
        }

        Changed?.Invoke();
    }

    public void NotifyRemoved(string name)
    {
        store.Set(name, null);
        lock (_lock)
        {
            _tracks.Remove(name);
        }

        Changed?.Invoke();
    }

    public async Task ProcessAsync(IReadOnlyList<ContainerObservation> snapshot, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var changed = false;
            var due = new List<string>();
            var now = clock.GetUtcNow();
            var first = !_initialized;
            _initialized = true;
            var byName = snapshot.ToDictionary(o => o.Name, StringComparer.Ordinal);

            foreach (var (name, entry) in store.Snapshot())
            {
                if (!byName.TryGetValue(name, out var obs))
                {
                    changed |= ForgetIfGone(name);
                    continue;
                }

                _absent.Remove(name);
                var policy = RestartPolicy.Parse(entry.Policy);
                if (policy.IsNone)
                {
                    continue;
                }

                var userStopped = entry.UserStopped;
                if (first && userStopped && policy.Mode != RestartMode.UnlessStopped)
                {
                    // A stop from an earlier app run only sticks for unless-stopped.
                    store.Set(name, entry with { UserStopped = false });
                    userStopped = false;
                }

                changed |= Evaluate(name, policy, obs, userStopped, first, now, due);
            }

            foreach (var name in _absent.Keys.ToList())
            {
                if (!store.TryGet(name, out _))
                {
                    _absent.Remove(name);
                }
            }

            foreach (var name in due)
            {
                changed = true;
                try
                {
                    await actions.StartAsync(name, ct);
                    lock (_lock)
                    {
                        if (_tracks.TryGetValue(name, out var t))
                        {
                            t.Pending = null;
                            t.LastError = null;
                            t.LastStart = clock.GetUtcNow();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    lock (_lock)
                    {
                        if (_tracks.TryGetValue(name, out var t))
                        {
                            t.Pending = null;
                            t.LastError = e.Message;
                        }
                    }
                }
            }

            if (changed)
            {
                Changed?.Invoke();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool ForgetIfGone(string name)
    {
        _absent[name] = _absent.GetValueOrDefault(name) + 1;
        if (_absent[name] < _o.AbsentSnapshotsBeforeForget)
        {
            return false;
        }

        _absent.Remove(name);
        store.Set(name, null);
        lock (_lock)
        {
            _tracks.Remove(name);
        }

        return true;
    }

    private bool Evaluate(
        string name, RestartPolicy policy, ContainerObservation obs, bool userStopped, bool first,
        DateTimeOffset now, List<string> due)
    {
        lock (_lock)
        {
            var track = _tracks.TryGetValue(name, out var existing) ? existing : _tracks[name] = new Track();
            var before = (track.Pending, track.GaveUp, track.Attempts);

            if (IsRunning(obs.State))
            {
                track.Pending = null;
                track.RunningSince ??= now;
                if (now - track.RunningSince >= _o.StableAfter && (track.Attempts > 0 || track.GaveUp))
                {
                    track.Attempts = 0;
                    track.GaveUp = false;
                    track.LastError = null;
                }

                return before != (track.Pending, track.GaveUp, track.Attempts);
            }

            track.RunningSince = null;
            if (!IsDown(obs.State) || userStopped)
            {
                track.Pending = null;
                return before != (track.Pending, track.GaveUp, track.Attempts);
            }

            if (track.LastStart is { } started && now - started < _o.StartGrace)
            {
                // A snapshot taken before our start landed still shows the old exit.
                return false;
            }

            var wanted = policy.Mode switch
            {
                RestartMode.OnFailure => obs.ExitCode != 0,
                _ => true,
            };
            if (!wanted || track.GaveUp)
            {
                track.Pending = null;
                return before != (track.Pending, track.GaveUp, track.Attempts);
            }

            if (track.Pending is null)
            {
                if (track.Attempts >= Limit(policy))
                {
                    track.GaveUp = true;
                    return true;
                }

                track.Attempts++;
                var delay = first ? TimeSpan.Zero : Backoff(track.Attempts);
                track.Pending = now + delay;
            }

            if (now >= track.Pending)
            {
                due.Add(name);
            }

            return before != (track.Pending, track.GaveUp, track.Attempts);
        }
    }

    public TimeSpan Backoff(int attempt)
    {
        var factor = Math.Pow(2, Math.Max(0, attempt - 1));
        var ms = Math.Min(_o.BaseDelay.TotalMilliseconds * factor, _o.MaxDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(ms);
    }

    private int Limit(RestartPolicy policy) =>
        policy is { Mode: RestartMode.OnFailure, MaxRetries: > 0 } ? policy.MaxRetries : _o.MaxAttempts;

    private static bool IsRunning(string state) => state.Equals("running", StringComparison.OrdinalIgnoreCase);

    // "created" containers were never started, "paused"/"restarting" are still alive: none of them are crashes.
    private static bool IsDown(string state) =>
        state.Equals("exited", StringComparison.OrdinalIgnoreCase)
        || state.Equals("stopped", StringComparison.OrdinalIgnoreCase)
        || state.Equals("dead", StringComparison.OrdinalIgnoreCase);

    private sealed class Track
    {
        public int Attempts;
        public bool GaveUp;
        public DateTimeOffset? Pending;
        public DateTimeOffset? RunningSince;
        public DateTimeOffset? LastStart;
        public string? LastError;
    }
}