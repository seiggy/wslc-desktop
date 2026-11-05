using WslcDesktop.Backends;
using WslcDesktop.Sessions;

namespace WslcDesktop.Tests.Backends;

public class BackendRouterTests
{
    private static readonly SessionHandle App = new("WslcDesktop", IsThisApp: true);

    [Fact]
    public void An_owned_app_session_uses_the_sdk_backend()
    {
        var sdk = new InMemoryBackend("WslcDesktop");
        var router = new BackendRouter(sdk, name => new InMemoryBackend(name));

        Assert.Same(sdk, router.For(App));
    }

    [Fact]
    public void An_attached_app_session_uses_the_cli_backend_for_its_name()
    {
        var router = new BackendRouter(owned: null, name => new InMemoryBackend(name));

        Assert.Equal("WslcDesktop", router.For(App).Session);
    }

    [Fact]
    public void Other_sessions_always_use_the_cli_backend_even_when_the_app_owns_its_own()
    {
        var sdk = new InMemoryBackend("WslcDesktop");
        var router = new BackendRouter(sdk, name => new InMemoryBackend(name));

        var backend = router.For(new SessionHandle("lab", IsThisApp: false));

        Assert.NotSame(sdk, backend);
        Assert.Equal("lab", backend.Session);
    }

    [Fact]
    public void The_same_session_resolves_to_one_backend_instance()
    {
        var router = new BackendRouter(owned: null, name => new InMemoryBackend(name));

        Assert.Same(router.For(new SessionHandle("lab", false)), router.For(new SessionHandle("lab", false)));
    }
}
