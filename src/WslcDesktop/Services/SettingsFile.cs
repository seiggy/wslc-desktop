using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace WslcDesktop.Services;

public sealed record SettingDef(string Key, string Section, string Label, string Help, string[]? Choices = null);

/// <summary>
/// Reads and edits wslc's settings.yaml line by line so comments, ordering and unknown keys survive a save.
/// </summary>
public sealed class SettingsFile
{
    public const string Default = "default";

    public static readonly SettingDef[] Definitions =
    [
        new("cpuCount", "session", "CPU count", "Virtual CPUs for the default session. Default: all available CPUs."),
        new("memorySize", "session", "Memory", "Memory limit, e.g. 2GB or 512MB. Default: half of the available memory."),
        new("maxStorageSize", "session", "Max storage", "Maximum disk image size, e.g. 500GB. Default: 1TB."),
        new("storagePath", "session", "Storage path", "Absolute base folder for the default session's storage. Changing it creates a new, empty session; existing data isn't moved."),
        new("defaultBindingAddress", "session", "Default binding address", "Host address published ports bind to when none is given. Default: 127.0.0.1."),
        new("hostLoopback", "session", "Host loopback name", "DNS name that resolves to the host loopback address. Use \"none\" to disable. Default: host.wslc.internal."),
        new("idleTimeout", "session", "Idle timeout (seconds)", "Seconds an idle session VM stays running before it is torn down. Default: 30."),
        new("credentialStore", string.Empty, "Credential store", "Where registry credentials are kept.", ["wincred", "file"]),
    ];

    private static readonly Regex Memory = new(@"^\d+(\.\d+)?\s?(KB|MB|GB|TB)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Host = new(@"^[A-Za-z0-9]([A-Za-z0-9\-]*[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9\-]*[A-Za-z0-9])?)*$", RegexOptions.Compiled);

    private const string Template =
        "# wslc user settings\n# https://aka.ms/wslc-settings\n# All settings support string value \"default\" which uses built-in defaults.\n\nsession:\n";

    private List<string> _lines = [];
    private string _newline = "\r\n";

    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "wslc", "settings.yaml");

    public string Path { get; }

    public bool Exists => File.Exists(Path);

    public SettingsFile(string? path = null) => Path = path ?? DefaultPath;

    public void Load()
    {
        var text = File.Exists(Path) ? File.ReadAllText(Path) : string.Empty;
        _newline = text.Contains("\r\n") || text.Length == 0 ? "\r\n" : "\n";
        _lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        if (_lines.Count > 0 && _lines[^1].Length == 0)
        {
            _lines.RemoveAt(_lines.Count - 1);
        }
    }

    /// <summary>Returns the active value, or "default" when the key is unset or commented out.</summary>
    public string Get(SettingDef def)
    {
        var i = FindActive(def);
        return i < 0 ? Default : ValueOf(_lines[i]);
    }

    public bool IsSet(SettingDef def) => FindActive(def) >= 0;

    public static string? Validate(SettingDef def, string raw)
    {
        var v = raw.Trim();
        if (v.Length == 0)
        {
            return "Enter a value, or \"default\".";
        }

        if (v.Equals(Default, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        switch (def.Key)
        {
            case "cpuCount":
                return int.TryParse(v, out var n) && n >= 1 && n <= Environment.ProcessorCount
                    ? null
                    : $"Use a whole number from 1 to {Environment.ProcessorCount}.";
            case "memorySize":
            case "maxStorageSize":
                return Memory.IsMatch(v) ? null : "Use a size such as 512MB, 4GB or 1TB.";
            case "storagePath":
                return System.IO.Path.IsPathFullyQualified(v) && v.IndexOfAny(System.IO.Path.GetInvalidPathChars()) < 0
                    ? null
                    : "Use an absolute path such as D:\\data.";
            case "defaultBindingAddress":
                return IPAddress.TryParse(v, out _) ? null : "Use an IP address such as 127.0.0.1 or 0.0.0.0.";
            case "hostLoopback":
                return v.Equals("none", StringComparison.OrdinalIgnoreCase) || Host.IsMatch(v)
                    ? null
                    : "Use a DNS name, or \"none\" to disable.";
            case "idleTimeout":
                return int.TryParse(v, out var s) && s >= 0 ? null : "Use a number of seconds (0 or more).";
            case "credentialStore":
                return def.Choices!.Contains(v, StringComparer.OrdinalIgnoreCase) ? null : "Choose wincred or file.";
            default:
                return null;
        }
    }

    /// <summary>Applies a value in memory; "default" comments an active line out rather than deleting it.</summary>
    public void Set(SettingDef def, string raw)
    {
        var value = raw.Trim();
        var isDefault = value.Equals(Default, StringComparison.OrdinalIgnoreCase);
        var indent = def.Section.Length > 0 ? "  " : string.Empty;
        var active = FindActive(def);
        if (active >= 0)
        {
            _lines[active] = isDefault ? $"{indent}# {def.Key}: {Default}" : $"{indent}{def.Key}: {value}";
            return;
        }

        if (isDefault)
        {
            return;
        }

        var line = $"{indent}{def.Key}: {value}";
        var commented = FindCommented(def);
        if (commented >= 0)
        {
            _lines[commented] = line;
            return;
        }

        if (def.Section.Length == 0)
        {
            _lines.Add(string.Empty);
            _lines.Add(line);
            return;
        }

        var (start, end) = SectionRange(def.Section);
        if (start < 0)
        {
            _lines.Add(string.Empty);
            _lines.Add($"{def.Section}:");
            _lines.Add(line);
            return;
        }

        _lines.Insert(end, line);
    }

    public void ResetAll()
    {
        foreach (var def in Definitions)
        {
            Set(def, Default);
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var text = string.Join(_newline, _lines) + _newline;
        var temp = Path + ".tmp";
        File.WriteAllText(temp, text, new UTF8Encoding(false));
        File.Move(temp, Path, overwrite: true);
    }

    public void LoadOrCreateTemplate()
    {
        Load();
        if (_lines.Count == 0)
        {
            _lines = Template.TrimEnd('\n').Split('\n').ToList();
        }
    }

    private static string ValueOf(string line)
    {
        var colon = line.IndexOf(':');
        var v = line[(colon + 1)..];
        var hash = v.IndexOf(" #", StringComparison.Ordinal);
        if (hash >= 0)
        {
            v = v[..hash];
        }

        return v.Trim().Trim('"', '\'');
    }

    private (int Start, int End) SectionRange(string section)
    {
        var start = _lines.FindIndex(l => l.TrimEnd() == $"{section}:");
        if (start < 0)
        {
            return (-1, -1);
        }

        // The section runs until the next unindented, non-comment line; trailing blanks and comments stay outside.
        var end = start + 1;
        var lastContent = start;
        for (; end < _lines.Count; end++)
        {
            var l = _lines[end];
            if (l.Length > 0 && !char.IsWhiteSpace(l[0]) && !l.StartsWith('#'))
            {
                break;
            }

            if (l.StartsWith("  ", StringComparison.Ordinal) && l.Trim().Length > 0)
            {
                lastContent = end;
            }
        }

        return (start, lastContent + 1);
    }

    private int FindActive(SettingDef def)
    {
        var prefix = def.Section.Length > 0 ? "  " : string.Empty;
        var inSection = def.Section.Length == 0;
        for (var i = 0; i < _lines.Count; i++)
        {
            var l = _lines[i];
            if (def.Section.Length > 0 && l.Length > 0 && !char.IsWhiteSpace(l[0]) && !l.StartsWith('#'))
            {
                inSection = l.TrimEnd() == $"{def.Section}:";
                continue;
            }

            if (inSection && l.StartsWith(prefix + def.Key + ":", StringComparison.Ordinal)
                && (def.Section.Length > 0 ? l.Length > 2 && !char.IsWhiteSpace(l[2]) : true))
            {
                return i;
            }
        }

        return -1;
    }

    private int FindCommented(SettingDef def)
    {
        var prefix = def.Section.Length > 0 ? "  " : string.Empty;
        return _lines.FindIndex(l => l.StartsWith($"{prefix}# {def.Key}:", StringComparison.Ordinal));
    }
}
