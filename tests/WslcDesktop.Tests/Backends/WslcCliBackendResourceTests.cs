using WslcDesktop.Backends;
using WslcDesktop.Resources;

namespace WslcDesktop.Tests.Backends;

public class WslcCliBackendResourceTests
{
    private readonly FakeWslcProcess _process = new();

    private WslcCliBackend Backend(string session = "lab") => new(_process, session);

    [Fact]
    public async Task Creating_a_vhd_volume_passes_its_size()
    {
        await Backend().CreateVolumeAsync("data", "vhd", 1024);

        Assert.Equal(["--session", "lab", "volume", "create", "--driver", "vhd", "--opt", "SizeBytes=1024", "data"], _process.LastArgs);
    }

    [Fact]
    public async Task Creating_a_network_adds_optional_flags_only_when_given()
    {
        await Backend().CreateNetworkAsync("back", "bridge", "10.1.0.0/16", "10.1.0.1", isInternal: true);

        Assert.Equal(
            ["--session", "lab", "network", "create", "--driver", "bridge", "--subnet", "10.1.0.0/16",
             "--gateway", "10.1.0.1", "--internal", "back"],
            _process.LastArgs);

        await Backend().CreateNetworkAsync("plain", "bridge", "", "", isInternal: false);
        Assert.Equal(["--session", "lab", "network", "create", "--driver", "bridge", "plain"], _process.LastArgs);
    }

    [Theory]
    [InlineData(ResourceKind.Volume, "volume")]
    [InlineData(ResourceKind.Network, "network")]
    public async Task Removing_a_resource_uses_its_noun(ResourceKind kind, string noun)
    {
        await Backend().RemoveResourceAsync(kind, "x");

        Assert.Equal(["--session", "lab", noun, "remove", "x"], _process.LastArgs);
    }

    [Fact]
    public async Task Pruning_volumes_removes_all_unused_and_networks_just_unused()
    {
        await Backend().PruneResourcesAsync(ResourceKind.Volume);
        Assert.Equal(["--session", "lab", "volume", "prune", "--all", "--force"], _process.LastArgs);

        await Backend().PruneResourcesAsync(ResourceKind.Network);
        Assert.Equal(["--session", "lab", "network", "prune", "--force"], _process.LastArgs);
    }

    [Fact]
    public async Task Connecting_a_network_adds_the_alias_when_given()
    {
        await Backend().ConnectNetworkAsync("back", "web", "frontend");
        Assert.Equal(["--session", "lab", "network", "connect", "--network-alias", "frontend", "back", "web"], _process.LastArgs);

        await Backend().ConnectNetworkAsync("back", "web", "");
        Assert.Equal(["--session", "lab", "network", "connect", "back", "web"], _process.LastArgs);
    }

    [Fact]
    public async Task Disconnecting_a_network_names_network_then_container()
    {
        await Backend().DisconnectNetworkAsync("back", "web");
        Assert.Equal(["--session", "lab", "network", "disconnect", "back", "web"], _process.LastArgs);
    }

    [Fact]
    public async Task Listing_volumes_marks_which_containers_use_them()
    {
        _process
            .Returns("""{"Name":"data","Driver":"local","Mountpoint":"/v/data","Scope":"local"}""")
            .Returns("""{"Names":"web","Image":"nginx","ID":"abc","State":"running","Command":"","Ports":"","Labels":"","Status":"Up"}""")
            .Returns("""[{"Mounts":[{"Type":"volume","Name":"data"}]}]""");

        var rows = await Backend().ListResourcesAsync(ResourceKind.Volume);

        var row = Assert.Single(rows);
        Assert.Equal("data", row.Name);
        Assert.Equal(["web"], row.UsedBy);
    }

    [Fact]
    public async Task Built_in_networks_cannot_be_removed()
    {
        _process
            .Returns("""{"Name":"bridge","Driver":"bridge","Scope":"local","Internal":"false"}""")
            .Returns(string.Empty);

        var rows = await Backend().ListResourcesAsync(ResourceKind.Network);

        Assert.False(Assert.Single(rows).CanRemove);
    }
}
