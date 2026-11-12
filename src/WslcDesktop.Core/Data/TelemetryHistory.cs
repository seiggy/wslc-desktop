using WslcDesktop.Containers;

namespace WslcDesktop.Data;

/// <summary>
/// Rolling CPU/memory/I/O history per container, appended to on every stats read, so charts have the samples taken
/// while the container list was showing too. A container that stops loses its history: old samples would mislead
/// the chart when it starts again.
/// </summary>
public sealed class TelemetryHistory
{
    public const int Capacity = 150;

    private readonly Dictionary<(string Session, string Container), List<StatsSample>> _history = new(new KeyComparer());
    private readonly object _lock = new();

    /// <summary>Appends a read and forgets containers of the session that aren't running any more.</summary>
    public void Record(string session, IReadOnlyCollection<string> running, IReadOnlyDictionary<string, StatsSample> samples)
    {
        lock (_lock)
        {
            foreach (var key in _history.Keys.Where(k => Same(k.Session, session) && !running.Contains(k.Container)).ToList())
            {
                _history.Remove(key);
            }

            foreach (var (name, sample) in samples)
            {
                if (!running.Contains(name))
                {
                    continue;
                }

                if (!_history.TryGetValue((session, name), out var list))
                {
                    _history[(session, name)] = list = [];
                }

                list.Add(sample);
                if (list.Count > Capacity)
                {
                    list.RemoveAt(0);
                }
            }
        }
    }

    public void Forget(string session)
    {
        lock (_lock)
        {
            foreach (var key in _history.Keys.Where(k => Same(k.Session, session)).ToList())
            {
                _history.Remove(key);
            }
        }
    }

    public IReadOnlyList<StatsSample> GetHistory(string session, string container)
    {
        lock (_lock)
        {
            return _history.TryGetValue((session, container), out var list) ? list.ToArray() : [];
        }
    }

    public StatsSample? GetLatest(string session, string container)
    {
        lock (_lock)
        {
            return _history.TryGetValue((session, container), out var list) && list.Count > 0 ? list[^1] : null;
        }
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // Session names compare case-insensitively everywhere else; container names are exact.
    private sealed class KeyComparer : IEqualityComparer<(string Session, string Container)>
    {
        public bool Equals((string Session, string Container) x, (string Session, string Container) y) =>
            Same(x.Session, y.Session) && string.Equals(x.Container, y.Container, StringComparison.Ordinal);

        public int GetHashCode((string Session, string Container) k) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(k.Session), StringComparer.Ordinal.GetHashCode(k.Container));
    }
}
