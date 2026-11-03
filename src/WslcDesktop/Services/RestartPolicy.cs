using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WslcDesktop.Services;

public enum RestartMode
{
    No,
    Always,
    OnFailure,
    UnlessStopped,
}

/// <summary>An app-level restart policy: no, always, on-failure[:max], unless-stopped.</summary>
public readonly record struct RestartPolicy(RestartMode Mode, int MaxRetries = 0)
{
    public static RestartPolicy None => new(RestartMode.No);

    public bool IsNone => Mode == RestartMode.No;

    public static bool TryParse(string? text, out RestartPolicy policy)
    {
        policy = None;
        var t = (text ?? string.Empty).Trim().Trim('"', '\'').ToLowerInvariant();
        switch (t)
        {
            case "" or "no" or "false":
                return true;
            case "always":
                policy = new RestartPolicy(RestartMode.Always);
                return true;
            case "unless-stopped":
                policy = new RestartPolicy(RestartMode.UnlessStopped);
                return true;
            case "on-failure":
                policy = new RestartPolicy(RestartMode.OnFailure);
                return true;
        }

        var m = Regex.Match(t, @"^on-failure:(\d+)$");
        if (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var max))
        {
            policy = new RestartPolicy(RestartMode.OnFailure, max);
            return true;
        }

        return false;
    }

    public static RestartPolicy Parse(string? text) => TryParse(text, out var p) ? p : None;

    // The same spelling `docker run --restart` and compose use.
    public override string ToString() => Mode switch
    {
        RestartMode.Always => "always",
        RestartMode.UnlessStopped => "unless-stopped",
        RestartMode.OnFailure => MaxRetries > 0 ? $"on-failure:{MaxRetries}" : "on-failure",
        _ => "no",
    };
}

/// <summary>What the engine knows about one container's restart handling, for display.</summary>
public enum RestartState
{
    /// <summary>No policy, or nothing pending.</summary>
    Idle,

    /// <summary>The container exited and a restart is scheduled.</summary>
    Restarting,

    /// <summary>Restarts kept failing; the engine stopped trying.</summary>
    CrashLoop,
}

public sealed record RestartInfo(
    RestartPolicy Policy, RestartState State, int Attempts, int MaxAttempts, DateTimeOffset? NextAttemptAt, string? LastError)
{
    public static RestartInfo Empty { get; } = new(RestartPolicy.None, RestartState.Idle, 0, 0, null, null);
}

public sealed record RestartEntry(string Policy, bool UserStopped);

public interface IRestartStore
{
    IReadOnlyDictionary<string, RestartEntry> Snapshot();

    void Set(string name, RestartEntry? entry);
}

/// <summary>Per-container restart policies in %LOCALAPPDATA%\WslcDesktop\restart-policies.json, keyed by container name.</summary>
public sealed class RestartPolicyStore : IRestartStore
{
    private static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WslcDesktop", "restart-policies.json");

    private readonly object _lock = new();
    private readonly string _path;
    private readonly Dictionary<string, RestartEntry> _entries;

    public RestartPolicyStore(string? path = null)
    {
        _path = path ?? DefaultPath;
        _entries = Load(_path);
    }

    public IReadOnlyDictionary<string, RestartEntry> Snapshot()
    {
        lock (_lock)
        {
            return new Dictionary<string, RestartEntry>(_entries, StringComparer.Ordinal);
        }
    }

    public void Set(string name, RestartEntry? entry)
    {
        lock (_lock)
        {
            if (entry is null)
            {
                if (!_entries.Remove(name))
                {
                    return;
                }
            }
            else
            {
                _entries[name] = entry;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var temp = _path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temp, _path, overwrite: true);
            }
            catch (IOException)
            {
                // The in-memory policy still applies for this run.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static Dictionary<string, RestartEntry> Load(string path)
    {
        try
        {
            if (File.Exists(path)
                && JsonSerializer.Deserialize<Dictionary<string, RestartEntry>>(File.ReadAllText(path)) is { } map)
            {
                return new Dictionary<string, RestartEntry>(map.Where(kv => kv.Value is not null), StringComparer.Ordinal);
            }
        }
        catch (Exception)
        {
            // A corrupt file starts empty rather than blocking startup.
        }

        return new Dictionary<string, RestartEntry>(StringComparer.Ordinal);
    }
}

public sealed record ContainerObservation(string Name, string State, int? ExitCode);

public interface IRestartActions
{
    Task StartAsync(string name, CancellationToken ct);
}

public sealed class RestartOptions
{
    /// <summary>Consecutive restarts allowed before the engine declares a crash loop.</summary>
    public int MaxAttempts { get; init; } = 5;

    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>A container that stays up this long is healthy again and its attempt counter resets.</summary>
    public TimeSpan StableAfter { get; init; } = TimeSpan.FromSeconds(60);

    public TimeSpan StartGrace { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Snapshots a container must be missing from before its policy is forgotten.</summary>
    public int AbsentSnapshotsBeforeForget { get; init; } = 2;
}

/// <summary>Pulls the exit code out of a `wslc list` status such as "Exited (137) 4 seconds ago".</summary>
public static partial class ContainerStatusText
{
    public static int? ExitCode(string? status)
    {
        var m = ExitRegex().Match(status ?? string.Empty);
        return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code)
            ? code
            : null;
    }

    [GeneratedRegex(@"\((-?\d+)\)")]
    private static partial Regex ExitRegex();
}

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
        store.Snapshot().TryGetValue(name, out var e) ? RestartPolicy.Parse(e.Policy) : RestartPolicy.None;

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

            var state = t.GaveUp ? RestartState.CrashLoop : t.Pending is not null ? RestartState.Restarting : RestartState.Idle;
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
        if (store.Snapshot().TryGetValue(name, out var e) && !e.UserStopped)
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
        if (store.Snapshot().TryGetValue(name, out var e) && e.UserStopped)
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

            foreach (var name in _absent.Keys.Where(n => !store.Snapshot().ContainsKey(n)).ToList())
            {
                _absent.Remove(name);
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
