using WslcDesktop.Sessions;

namespace WslcDesktop.Backends;

/// <summary>
/// The one place that decides how a session is driven: the SDK adapter for the session this run owns, wslc.exe
/// for an attached app session and for every other session.
/// </summary>
public sealed class BackendRouter(ISessionBackend? owned, Func<string, ISessionBackend> cli)
{
    private readonly Dictionary<string, ISessionBackend> _cli = new(StringComparer.OrdinalIgnoreCase);

    public ISessionBackend For(SessionHandle session)
    {
        if (session.IsThisApp && owned is not null)
        {
            return owned;
        }

        lock (_cli)
        {
            if (!_cli.TryGetValue(session.Name, out var backend))
            {
                _cli[session.Name] = backend = cli(session.Name);
            }

            return backend;
        }
    }
}
