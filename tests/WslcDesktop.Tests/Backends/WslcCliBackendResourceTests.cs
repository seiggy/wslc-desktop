using WslcDesktop.Backends;
using WslcDesktop.Resources;

namespace WslcDesktop.Tests.Backends;

public class WslcCliBackendResourceTests
{
    private readonly FakeWslcProcess _process = new();

    private WslcCliBackend Backend(string session = "lab") => new(_process, session, _ => null);

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

    [Fact]
    public async Task Usage_inspects_every_container_in_one_call()
    {
        _process
            .Returns("""{"Name":"back","Driver":"bridge","Scope":"local","Internal":"false"}""")
            .Returns("""
                {"Names":"web","Image":"nginx","ID":"a","State":"running","Command":"","Ports":"","Labels":"","Status":"Up"}
                {"Names":"db","Image":"postgres","ID":"b","State":"running","Command":"","Ports":"","Labels":"","Status":"Up"}
                {"Names":"idle","Image":"alpine","ID":"c","State":"exited","Command":"","Ports":"","Labels":"","Status":"Exited"}
                """)
            .Returns("""
                [{"Name":"/web","NetworkSettings":{"Networks":{"back":{}}}},
                 {"Name":"/db","NetworkSettings":{"Networks":{"back":{},"bridge":{}}}},
                 {"Name":"/idle","NetworkSettings":{"Networks":{}}}]
                """);

        var row = Assert.Single(await Backend().ListResourcesAsync(ResourceKind.Network));

        Assert.Equal(["web", "db"], row.UsedBy);
        var inspects = _process.Calls.Where(c => c.Args.Contains("inspect")).ToList();
        var inspect = Assert.Single(inspects);
        Assert.Equal(["--session", "lab", "inspect", "--type", "container", "--format", "json", "web", "db", "idle"], inspect.Args);
    }

    [Fact]
    public async Task A_failed_batch_inspect_falls_back_to_one_inspect_per_container()
    {
        // The volume listing and the container listing, then a batch that fails because a container was just removed.
        _process
            .Returns("""{"Name":"data","Driver":"local","Mountpoint":"/v/data","Scope":"local"}""")
            .Returns("""
                {"Names":"web","Image":"nginx","ID":"a","State":"running","Command":"","Ports":"","Labels":"","Status":"Up"}
                {"Names":"gone","Image":"nginx","ID":"b","State":"exited","Command":"","Ports":"","Labels":"","Status":"Exited"}
                """)
            .Throws(new InvalidOperationException("Object not found: gone"))
            .Returns("""[{"Mounts":[{"Type":"volume","Name":"data"}]}]""")
            .Throws(new InvalidOperationException("Object not found: gone"));

        var row = Assert.Single(await Backend().ListResourcesAsync(ResourceKind.Volume));

        Assert.Equal(["web"], row.UsedBy);
        Assert.Equal(3, _process.Calls.Count(c => c.Args.Contains("inspect")));
    }

    [Fact]
    public async Task No_inspect_runs_when_the_session_has_no_containers()
    {
        _process
            .Returns("""{"Name":"data","Driver":"local","Mountpoint":"/v/data","Scope":"local"}""")
            .Returns(string.Empty);

        var row = Assert.Single(await Backend().ListResourcesAsync(ResourceKind.Volume));

        Assert.Empty(row.UsedBy);
        Assert.DoesNotContain(_process.Calls, c => c.Args.Contains("inspect"));
    }

    [Fact]
    public async Task Network_names_do_not_inspect_containers()
    {
        _process.Returns("""
            {"Name":"bridge","Driver":"bridge","Scope":"local","Internal":"false"}
            {"Name":"back","Driver":"bridge","Scope":"local","Internal":"true"}
            """);

        var names = await Backend().ListNetworkNamesAsync();

        Assert.Equal(["bridge", "back"], names);
        Assert.Equal(["--session", "lab", "network", "list", "--format", "json"], Assert.Single(_process.Calls).Args);
    }
}
