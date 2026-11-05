
using WslcDesktop.Compose.Execution;
using WslcDesktop.Containers;
using WslcDesktop.Sessions;

namespace WslcDesktop.Tests.Containers;

public class ContainerListingTests
{
    private static ContainerRow C(string name, string stack = "") =>
        new(name, "alpine", "id", "running", "", "", stack.Length > 0 ? $"{ComposeLabels.Project}={stack}" : "");

    private static SessionSnapshot<ContainerRow> Snap(params (string Session, ContainerRow Row)[] items) =>
        new(
            items.Select(i => new SessionEntry<ContainerRow>(i.Session, i.Session == "WslcDesktop", i.Row)).ToList(),
            ["WslcDesktop", "cli"],
            []);

    [Fact]
    public void Same_name_in_two_sessions_has_distinct_keys()
    {
        var list = ContainerListing.Build(Snap(("WslcDesktop", C("web")), ("cli", C("web"))), null);

        Assert.Equal(["WslcDesktop|web", "cli|web"], list.Select(e => e.Key));
        Assert.True(list[0].IsThisApp);
        Assert.False(list[1].IsThisApp);
    }

    [Fact]
    public void Orders_by_session_then_standalone_before_stacks()
    {
        var list = ContainerListing.Build(
            Snap(("cli", C("z")), ("WslcDesktop", C("s1", "app")), ("WslcDesktop", C("solo"))),
            null);

        Assert.Equal(["WslcDesktop|solo", "WslcDesktop|s1", "cli|z"], list.Select(e => e.Key));
    }

    [Fact]
    public void Stack_header_is_on_first_member_and_counted_per_session()
    {
        var list = ContainerListing.Build(
            Snap(("WslcDesktop", C("a1", "app")), ("WslcDesktop", C("a2", "app")), ("cli", C("b1", "app"))),
            null);

        Assert.Equal("Stack: app (2)", list[0].Header);
        Assert.Equal(string.Empty, list[1].Header);
        Assert.Equal("Stack: app (1)", list[2].Header);
    }

    [Fact]
    public void Filter_limits_to_one_session()
    {
        var list = ContainerListing.Build(Snap(("WslcDesktop", C("a")), ("cli", C("b"))), "cli");

        Assert.Equal(["cli|b"], list.Select(e => e.Key));
    }
}
