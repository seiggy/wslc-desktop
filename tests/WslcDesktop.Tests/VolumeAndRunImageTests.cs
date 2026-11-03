using WslcDesktop.Services;

namespace WslcDesktop.Tests;

public class VolumeAndRunImageTests
{
    [Theory]
    [InlineData(10, SizeUnit.GB, 10L * 1024 * 1024 * 1024)]
    [InlineData(512, SizeUnit.MB, 512L * 1024 * 1024)]
    [InlineData(3, SizeUnit.MB, 3L * 1024 * 1024)]
    [InlineData(64, SizeUnit.TB, 64L * 1024 * 1024 * 1024 * 1024)]
    [InlineData(0.5, SizeUnit.GB, 512L * 1024 * 1024)]
    public void Valid_sizes_convert_to_bytes(double value, SizeUnit unit, long expected)
    {
        Assert.True(VolumeSize.TryToBytes(value, unit, out var bytes, out var error));
        Assert.Equal(expected, bytes);
        Assert.Empty(error);
    }

    [Theory]
    [InlineData(double.NaN, SizeUnit.GB)]
    [InlineData(0, SizeUnit.GB)]
    [InlineData(-1, SizeUnit.GB)]
    [InlineData(2, SizeUnit.MB)]
    [InlineData(65, SizeUnit.TB)]
    public void Invalid_sizes_give_a_friendly_error(double value, SizeUnit unit)
    {
        Assert.False(VolumeSize.TryToBytes(value, unit, out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Vhd_passes_SizeBytes_and_guest_does_not()
    {
        Assert.Equal(["--session", "s", "volume", "create", "--driver", "vhd", "--opt", "SizeBytes=1073741824", "v"],
            VolumeSize.CreateArguments("s", "v", "vhd", 1073741824));
        Assert.Equal(["--session", "s", "volume", "create", "--driver", "guest", "v"],
            VolumeSize.CreateArguments("s", "v", "guest", null));
    }

    [Fact]
    public void Reference_prefers_a_tag_then_the_id()
    {
        Assert.Equal("alpine:latest", RunImage.Reference(["alpine:latest", "alpine:3"], "sha256:abc"));
        Assert.Equal("sha256:abc", RunImage.Reference([], "sha256:abc"));
        Assert.Equal("sha256:abc", RunImage.Reference(["<none>:<none>"], "sha256:abc"));
    }

    [Theory]
    [InlineData("docker.io/library/nginx:latest", "nginx")]
    [InlineData("localhost:5000/team/my_app:1.0", "my_app")]
    [InlineData("alpine@sha256:abc", "alpine")]
    [InlineData("sha256:abc", "container")]
    public void Names_are_suggested_from_the_image(string reference, string expected) =>
        Assert.Equal(expected, RunImage.SuggestName(reference, []));

    [Fact]
    public void Suggested_names_avoid_existing_containers() =>
        Assert.Equal("nginx-3", RunImage.SuggestName("nginx:latest", ["nginx", "NGINX-2"]));
}
