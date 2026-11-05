using WslcDesktop.Backends;

namespace WslcDesktop.Tests.Backends;

public class WslcCliBackendImageTests
{
    private readonly FakeWslcProcess _process = new();

    private WslcCliBackend Backend(string session = "other") => new(_process, session, _ => null);

    [Fact]
    public async Task Deleting_an_image_runs_rmi_in_the_backends_session()
    {
        await Backend("lab").DeleteImageAsync("nginx:latest");

        Assert.Equal(["--session", "lab", "rmi", "nginx:latest"], _process.LastArgs);
    }

    [Fact]
    public async Task Pulling_an_image_runs_pull_in_the_backends_session()
    {
        await Backend("lab").PullImageAsync("docker.io/library/alpine:3", progress: null);

        Assert.Equal(["--session", "lab", "pull", "docker.io/library/alpine:3"], _process.LastArgs);
    }

    [Fact]
    public async Task Listing_images_parses_one_json_object_per_line()
    {
        _process.Returns("""
            {"Repository":"nginx","Tag":"latest","ID":"sha256:abc","Digest":"sha256:d","Size":"60MB","CreatedSince":"2 days ago","Containers":"1"}
            {"Repository":"<none>","Tag":"<none>","ID":"sha256:def","Digest":"","Size":"5MB","CreatedSince":"1 day ago","Containers":"0"}
            """);

        var images = await Backend("lab").ListImagesAsync();

        Assert.Equal(["--session", "lab", "images", "--no-trunc", "--format", "json"], _process.LastArgs);
        Assert.Equal(2, images.Count);
        Assert.Equal("nginx:latest", images[0].DisplayName);
        Assert.Equal(1, images[0].Containers);
        Assert.False(images[1].IsTagged);
    }
}
