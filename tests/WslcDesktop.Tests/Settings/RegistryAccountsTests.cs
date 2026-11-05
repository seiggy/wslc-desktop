using System.Text.Json;
using WslcDesktop.Backends;
using WslcDesktop.Resources;
using WslcDesktop.Sessions;
using WslcDesktop.Settings;
using WslcDesktop.Tests.Backends;

namespace WslcDesktop.Tests.Settings;

public class RegistryAccountsTests
{
    private static readonly SessionHandle App = new("WslcDesktop", IsThisApp: true);

    private readonly InMemoryBackend _backend = new("WslcDesktop");
    private readonly AppSettings _settings = new();
    private int _saves;
    private readonly RegistryAccounts _accounts;

    public RegistryAccountsTests()
    {
        var operations = new WslcOperations(new BackendRouter(null, _ => _backend), new NoEffects(), text => text);
        _accounts = new RegistryAccounts(operations.Registry, App, _settings, () => _saves++);
    }

    [Theory]
    [InlineData("", "pw", "Enter a username and a password or access token.")]
    [InlineData("zack", "", "Enter a username and a password or access token.")]
    [InlineData("  ", "pw", "Enter a username and a password or access token.")]
    public void Missing_details_are_rejected_with_a_message(string user, string password, string expected) =>
        Assert.Equal(expected, RegistryAccounts.Validate(user, password));

    [Fact]
    public void Complete_details_are_accepted() => Assert.Null(RegistryAccounts.Validate("zack", "pw"));

    [Fact]
    public async Task Signing_in_logs_in_through_the_app_session_remembers_the_server_and_saves()
    {
        await _accounts.SignInAsync("ghcr.io", "zack", "pw");

        Assert.Equal(["login ghcr.io"], _backend.Calls);
        Assert.Equal(["ghcr.io"], _accounts.Servers);
        Assert.Equal(1, _saves);
    }

    [Fact]
    public async Task Signing_in_to_the_same_server_twice_lists_it_once_ignoring_case()
    {
        await _accounts.SignInAsync("ghcr.io", "zack", "pw");
        await _accounts.SignInAsync("GHCR.IO", "zack", "pw2");

        Assert.Single(_accounts.Servers);
        Assert.Equal(1, _saves);
    }

    [Fact]
    public async Task A_blank_server_means_the_default_registry_and_is_remembered_as_such()
    {
        await _accounts.SignInAsync("", "zack", "pw");

        Assert.Equal([""], _accounts.Servers);
    }

    [Fact]
    public async Task A_failed_sign_in_lists_nothing_and_saves_nothing()
    {
        _backend.FailWith = new InvalidOperationException("unauthorized");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _accounts.SignInAsync("ghcr.io", "zack", "bad"));

        Assert.Empty(_accounts.Servers);
        Assert.Equal(0, _saves);
    }

    [Fact]
    public async Task Passwords_are_never_written_to_the_settings()
    {
        await _accounts.SignInAsync("ghcr.io", "zack", "s3cr3t-pw");

        Assert.DoesNotContain("s3cr3t-pw", JsonSerializer.Serialize(_settings));
    }

    [Fact]
    public async Task Signing_out_logs_out_forgets_the_server_ignoring_case_and_saves()
    {
        await _accounts.SignInAsync("ghcr.io", "zack", "pw");
        _backend.Calls.Clear();

        await _accounts.SignOutAsync("GHCR.io");

        Assert.Equal(["logout GHCR.io"], _backend.Calls);
        Assert.Empty(_accounts.Servers);
        Assert.Equal(2, _saves);
    }

    [Fact]
    public async Task A_failed_sign_out_keeps_the_server_listed()
    {
        await _accounts.SignInAsync("ghcr.io", "zack", "pw");
        _backend.FailWith = new InvalidOperationException("boom");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _accounts.SignOutAsync("ghcr.io"));

        Assert.Equal(["ghcr.io"], _accounts.Servers);
    }

    private sealed class NoEffects : IOperationEffects
    {
        public void ImagesChanged(string session) { }

        public void ContainersChanged(string session) { }

        public void ResourcesChanged(ResourceKind kind, string session) { }

        public void UserStarted(string container) { }

        public void UserStopped(string container) { }

        public void Removed(string container) { }
    }
}
