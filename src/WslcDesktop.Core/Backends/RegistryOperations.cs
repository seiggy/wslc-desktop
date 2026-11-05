using WslcDesktop.Sessions;

namespace WslcDesktop.Backends;

public sealed class RegistryOperations(BackendRouter router)
{
    public Task LoginAsync(SessionHandle session, string server, string username, string password) =>
        router.For(session).RegistryLoginAsync(server, username, password);

    public Task LogoutAsync(SessionHandle session, string server) => router.For(session).RegistryLogoutAsync(server);
}
