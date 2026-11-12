using WslcDesktop.Images;

namespace WslcDesktop.Tests.Images;

public class SessionImageTests
{
    private static ImageSummary Img(string repo) => new(repo, "latest", "sha256:aaaaaaaaaaaaaaaa", string.Empty, "1MB", "1 day ago", 0);

    [Fact]
    public void Untagged_images_cannot_be_copied_to_the_app_session()
    {
        var untagged = new SessionImage("other", false, new ImageSummary("<none>", "<none>", "sha256:abc", "", "1MB", "", 0));
        Assert.False(untagged.CanCopyToApp);
        Assert.True(new SessionImage("other", false, Img("alpine")).CanCopyToApp);
    }

    [Fact]
    public void Same_image_in_two_sessions_has_distinct_keys()
    {
        Assert.NotEqual(new SessionImage("WslcDesktop", true, Img("alpine")).Key, new SessionImage("other", false, Img("alpine")).Key);
    }
}
