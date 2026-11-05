using WslcDesktop.Backends;

namespace WslcDesktop.Tests.Backends;

public class WslcCliBackendRegistryTests
{
    private readonly FakeWslcProcess _process = new();

    private WslcCliBackend Backend() => new(_process, "lab", _ => null);

    [Fact]
    public async Task Login_sends_the_password_on_stdin_never_in_arguments()
    {
        await Backend().RegistryLoginAsync("ghcr.io", "zack", "hunter2");

        Assert.Equal(["--session", "lab", "registry", "login", "--username", "zack", "--password-stdin", "ghcr.io"], _process.LastArgs);
        Assert.Equal("hunter2", _process.Calls[^1].StandardInput);
        Assert.DoesNotContain("hunter2", _process.LastArgs);
    }

    [Fact]
    public async Task Login_without_a_server_targets_the_default_registry()
    {
        await Backend().RegistryLoginAsync("", "zack", "pw");

        Assert.Equal(["--session", "lab", "registry", "login", "--username", "zack", "--password-stdin"], _process.LastArgs);
    }

    [Fact]
    public async Task Logout_names_the_server()
    {
        await Backend().RegistryLogoutAsync("ghcr.io");

        Assert.Equal(["--session", "lab", "registry", "logout", "ghcr.io"], _process.LastArgs);
    }
}
