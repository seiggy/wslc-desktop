using WslcDesktop.Cli;
using WslcDesktop.Containers;
using WslcDesktop.Containers.Creation;
using WslcDesktop.Sessions;

namespace WslcDesktop.Backends;

public sealed class ContainerOperations(BackendRouter router, IOperationEffects effects, Func<string, string> redact)
{
    public Task<IReadOnlyList<ContainerRow>> ListAsync(SessionHandle session) => router.For(session).ListContainersAsync();

    public async Task CreateAsync(SessionHandle session, NewContainerSpec spec)
    {
        await router.For(session).CreateContainerAsync(spec);
        effects.ContainersChanged(session.Name);
    }

    public async Task CreateAsync(SessionHandle session, string name, string image, string[] commandLine, IReadOnlyDictionary<string, string> environment)
    {
        await router.For(session).CreateContainerAsync(name, image, commandLine, environment);
        effects.ContainersChanged(session.Name);
    }

    public async Task StartAsync(SessionHandle session, string name)
    {
        if (session.IsThisApp)
        {
            effects.UserStarted(name);
        }

        await router.For(session).StartContainerAsync(name);
        effects.ContainersChanged(session.Name);
    }

    public async Task StopAsync(SessionHandle session, string name)
    {
        if (session.IsThisApp)
        {
            effects.UserStopped(name);
        }

        await router.For(session).StopContainerAsync(name);
        effects.ContainersChanged(session.Name);
    }

    public async Task DeleteAsync(SessionHandle session, string name)
    {
        if (session.IsThisApp)
        {
            effects.UserStopped(name);
        }

        await router.For(session).DeleteContainerAsync(name);
        if (session.IsThisApp)
        {
            effects.Removed(name);
        }

        effects.ContainersChanged(session.Name);
    }

    public async Task<string> InspectAsync(SessionHandle session, string name) =>
        redact(await router.For(session).InspectContainerAsync(name));

    /// <summary>Streams a container's log, redacting stored secret values from every line, until it ends or <paramref name="ct"/> is cancelled.</summary>
    public Task StreamLogsAsync(SessionHandle session, string name, LogOptions options, Action<string> onLine, CancellationToken ct) =>
        router.For(session).StreamLogsAsync(name, options, line => onLine(redact(line)), ct);

    /// <summary>Starts an interactive (piped, non-TTY) shell in a running container; its output is redacted.</summary>
    public IShellProcess StartShell(SessionHandle session, string name, string shell, Action<string> onOutput, Action<int> onExit) =>
        router.For(session).StartShell(name, shell, text => onOutput(redact(text)), onExit);
}
