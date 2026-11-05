namespace WslcDesktop.Images;

/// <summary>An image as seen from one WSLc session. Image stores are per session, so the same image pulled in two sessions is two rows.</summary>
public sealed record SessionImage(string Session, bool IsThisApp, ImageSummary Image)
{
    /// <summary>Stable identity for in-place list updates. A multi-tag image appears once per tag, so the tag is part of the key.</summary>
    public string Key => $"{Session}|{Image.Id}|{Image.DisplayName}";

    /// <summary>True when "Run" can pull the image into the app's session: untagged images have no reference to pull.</summary>
    public bool CanCopyToApp => Image.IsTagged;
}
