using WslcDesktop.Backends;
using WslcDesktop.Sessions;

namespace WslcDesktop.Settings;

/// <summary>
/// Signs in and out of registries in the app session and remembers which servers were signed in to from this app.
/// Credentials are handed to wslc and never stored here.
/// </summary>
public sealed class RegistryAccounts(RegistryOperations registry, SessionHandle session, AppSettings settings, Action persist)
{
    /// <summary>Servers signed in to from this app; a blank entry is the default registry.</summary>
    public IReadOnlyList<string> Servers => settings.Registries;

    /// <summary>Returns why the details can't be used, or null when they can.</summary>
    public static string? Validate(string user, string password) =>
        user.Trim().Length == 0 || password.Length == 0 ? "Enter a username and a password or access token." : null;

    public async Task SignInAsync(string server, string user, string password)
    {
        await registry.LoginAsync(session, server, user, password);
        if (!settings.Registries.Contains(server, StringComparer.OrdinalIgnoreCase))
        {
            settings.Registries.Add(server);
            persist();
        }
    }

    public async Task SignOutAsync(string server)
    {
        await registry.LogoutAsync(session, server);
        settings.Registries.RemoveAll(s => s.Equals(server, StringComparison.OrdinalIgnoreCase));
        persist();
    }
}
