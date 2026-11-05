
namespace WslcDesktop.Compose.Planning.Resources;

/// <summary>Volume source/target parsing and host path resolution.</summary>
public static partial class VolumeSpec
{
    internal static string ResolveHostPath(string path, string directory)
    {
        if (path.StartsWith('~'))
        {
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.TrimStart('~', '/', '\\'));
        }

        return Path.GetFullPath(Path.Combine(directory, path));
    }

    internal static bool IsPath(string source) =>
        source.StartsWith('.') || source.StartsWith('/') || source.StartsWith('\\') || source.StartsWith('~')
        || (source.Length >= 2 && char.IsAsciiLetter(source[0]) && source[1] == ':');

    /// <summary>Splits "src:target[:mode]" while keeping a Windows drive letter ("C:\x") inside the source.</summary>
    public static (string Source, string? Target, bool ReadOnly) SplitVolume(string text)
    {
        var prefix = 0;
        if (text.Length >= 3 && char.IsAsciiLetter(text[0]) && text[1] == ':' && (text[2] is '\\' or '/'))
        {
            prefix = 2;
        }

        var parts = new List<string>();
        var start = 0;
        for (var i = prefix; i < text.Length; i++)
        {
            if (text[i] == ':')
            {
                parts.Add(text[start..i]);
                start = i + 1;
            }
        }

        parts.Add(text[start..]);
        if (parts.Count == 1)
        {
            return (parts[0], null, false);
        }

        var readOnly = parts.Count > 2 && parts[2].Split(',').Contains("ro");
        return (parts[0], parts[1], readOnly);
    }
}
