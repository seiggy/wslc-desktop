namespace WslcDesktop.Containers.Creation;

/// <summary>Helpers for creating a container from an image row or the image details page.</summary>
public static class RunImage
{
    /// <summary>The reference to prefill: the first tag, else the image ID (the only handle for an untagged image).</summary>
    public static string Reference(IEnumerable<string> tags, string id) =>
        tags.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t) && !t.Contains("<none>", StringComparison.Ordinal)) ?? id;

    /// <summary>Suggests a container name from the image, made unique against the existing names.</summary>
    public static string SuggestName(string reference, IEnumerable<string> existing)
    {
        var taken = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var repo = reference;
        if (!repo.StartsWith("sha256:", StringComparison.Ordinal))
        {
            var at = repo.IndexOf('@');
            if (at >= 0)
            {
                repo = repo[..at];
            }

            var slash = repo.LastIndexOf('/');
            var colon = repo.IndexOf(':', slash + 1);
            if (colon >= 0)
            {
                repo = repo[..colon];
            }

            repo = repo[(slash + 1)..];
        }
        else
        {
            repo = "container";
        }

        var clean = new string(repo.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-').ToArray()).Trim('-', '.', '_');
        if (clean.Length == 0)
        {
            clean = "container";
        }

        if (!taken.Contains(clean))
        {
            return clean;
        }

        for (var i = 2; ; i++)
        {
            var candidate = $"{clean}-{i}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
