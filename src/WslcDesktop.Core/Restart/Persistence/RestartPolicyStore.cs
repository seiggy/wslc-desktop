using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace WslcDesktop.Restart.Persistence;

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

    public bool TryGet(string name, [NotNullWhen(true)] out RestartEntry? entry)
    {
        lock (_lock)
        {
            return _entries.TryGetValue(name, out entry);
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