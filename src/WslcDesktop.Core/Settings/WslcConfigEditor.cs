namespace WslcDesktop.Settings;

/// <summary>
/// A draft over wslc's settings.yaml: edit values, see which ones are invalid, and save only what changed.
/// The file keeps its comments, ordering and unknown keys.
/// </summary>
public sealed class WslcConfigEditor(SettingsFile file)
{
    private readonly Dictionary<string, string> _drafts = new(StringComparer.Ordinal);

    public SettingsFile File { get; } = file;

    public bool FileExists => File.Exists;

    public bool IsValid => SettingsFile.Definitions.All(d => ErrorFor(d.Key) is null);

    public void Load()
    {
        File.LoadOrCreateTemplate();
        ResetDrafts();
    }

    /// <summary>Discards unsaved edits and re-reads the file.</summary>
    public void Revert() => Load();

    public string Get(string key) => _drafts.TryGetValue(key, out var v) ? v : SettingsFile.Default;

    public void SetDraft(string key, string value) => _drafts[key] = value;

    public string? ErrorFor(string key) =>
        SettingsFile.Validate(SettingsFile.Definitions.First(d => d.Key == key), Get(key));

    /// <summary>Writes the changed keys. Returns false, without touching the file, when any draft is invalid.</summary>
    public bool Save()
    {
        if (!IsValid)
        {
            return false;
        }

        foreach (var def in SettingsFile.Definitions)
        {
            var value = Get(def.Key).Trim();
            var unchanged = value.Equals(File.Get(def), StringComparison.Ordinal)
                || (value.Equals(SettingsFile.Default, StringComparison.OrdinalIgnoreCase) && !File.IsSet(def));
            if (!unchanged)
            {
                File.Set(def, value);
            }
        }

        File.Save();
        return true;
    }

    /// <summary>Sets every key back to "default" on disk (comments stay) and refreshes the drafts.</summary>
    public void Reset()
    {
        File.Load();
        File.ResetAll();
        File.Save();
        Load();
    }

    private void ResetDrafts()
    {
        _drafts.Clear();
        foreach (var def in SettingsFile.Definitions)
        {
            _drafts[def.Key] = File.Get(def);
        }
    }
}
