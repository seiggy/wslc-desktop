using System.Text.Json;

namespace WslcDesktop.Services;

/// <summary>
/// App preferences and resources for the app's own WslcDesktop session.
/// Session resources take effect after a restart; appearance and tray preferences apply immediately.
/// </summary>
public sealed class AppSettings
{
    public const uint DefaultCpuCount = 4;
    public const uint DefaultMemoryMb = 4096;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WslcDesktop", "settings.json");

    public uint CpuCount { get; set; } = DefaultCpuCount;

    public uint MemoryMb { get; set; } = DefaultMemoryMb;

    public bool EnableGpu { get; set; }

    public bool MinimizeToTray { get; set; }

    public bool CloseMinimizesToTray { get; set; }

    public string Theme { get; set; } = "System";

    // Null follows Windows; custom colors are opaque #RRGGBB.
    public string? AccentColor { get; set; }

    // Server names only; credentials live in wslc's credential store and are never read or written here.
    public List<string> Registries { get; set; } = [];

    public static AppSettings Load() => Load(FilePath);

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
                if (s is not null)
                {
                    s.CpuCount = Math.Clamp(s.CpuCount, 1, (uint)Environment.ProcessorCount);
                    s.MemoryMb = Math.Max(s.MemoryMb, 512);
                    s.Registries ??= [];
                    if (s.Theme is not ("System" or "Light" or "Dark"))
                    {
                        s.Theme = "System";
                    }

                    s.AccentColor = AccentPalette.Normalize(s.AccentColor);
                    return s;
                }
            }
        }
        catch (Exception)
        {
            // A corrupt file falls back to defaults rather than blocking startup.
        }

        return new AppSettings();
    }

    public void Save() => Save(FilePath);

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, overwrite: true);
    }
}
