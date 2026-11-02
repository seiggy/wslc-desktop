using Microsoft.UI.Xaml;

namespace WslcDesktop.Services;

public sealed record ImageSummary(
    string Repository,
    string Tag,
    string Id,
    string Digest,
    string Size,
    string CreatedSince,
    int Containers)
{
    private const string None = "<none>";

    public bool IsTagged => Repository != None && Tag != None;

    public string DisplayName => IsTagged ? $"{Repository}:{Tag}" : Repository != None ? Repository : "Untagged image";

    public string ShortId => Id.StartsWith("sha256:", StringComparison.Ordinal) && Id.Length >= 19 ? Id[7..19] : Id;

    // Untagged images can only be addressed by ID.
    public string Target => IsTagged ? DisplayName : Id;

    public string TagStatus => IsTagged ? "Tagged" : "Untagged";

    public Visibility TaggedVisibility => IsTagged ? Visibility.Visible : Visibility.Collapsed;

    public Visibility UntaggedVisibility => IsTagged ? Visibility.Collapsed : Visibility.Visible;

    public string UsedBy => Containers switch { 0 => "Unused", 1 => "1 container", _ => $"{Containers} containers" };
}

public sealed record ImageDetail(
    string Id,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Digests,
    string Created,
    string Size,
    string Os,
    string Architecture,
    string Author,
    string Comment,
    string User,
    string WorkingDir,
    IReadOnlyList<string> Entrypoint,
    IReadOnlyList<string> Cmd,
    IReadOnlyList<string> Env,
    IReadOnlyList<string> Ports,
    IReadOnlyList<string> Labels,
    IReadOnlyList<string> Layers,
    string RawJson);
