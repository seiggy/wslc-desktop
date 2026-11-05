using WslcDesktop.Localization;
namespace WslcDesktop.Images;

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

    public string DisplayName => IsTagged ? $"{Repository}:{Tag}" : Repository != None ? Repository : UiStrings.Get("UntaggedImage");

    public string ShortId => Id.StartsWith("sha256:", StringComparison.Ordinal) && Id.Length >= 19 ? Id[7..19] : Id;

    // Untagged images can only be addressed by ID.
    public string Target => IsTagged ? DisplayName : Id;

    public string TagStatus => IsTagged ? UiStrings.Get("Tagged") : UiStrings.Get("Untagged");

    public string UsedBy => Containers switch { 0 => UiStrings.Get("Unused"), 1 => UiStrings.Get("Container"), _ => UiStrings.Format("Containers", Containers) };
}
