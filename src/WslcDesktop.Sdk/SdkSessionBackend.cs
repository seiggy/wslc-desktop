using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.WSL.Containers;
using Windows.Networking;
using WslcDesktop.Backends;
using WslcDesktop.Cli;
using WslcDesktop.Containers;
using WslcDesktop.Containers.Creation;
using WslcDesktop.Images;
using WslcDesktop.Localization;
using WslcDesktop.Resources;
using WslcDesktop.Secrets;

namespace WslcDesktop.Sdk;

/// <summary>
/// Drives a session this process owns through the in-process SDK. The SDK cannot enumerate containers or volumes, and
/// its image listing lacks the per-image container count the UI shows, so those (and registries, logs, shells) go
/// through the wslc.exe fallback. Networks use the SDK, falling back on runtimes without network management.
/// </summary>
public sealed class SdkSessionBackend(Session session, ISessionBackend fallback) : ISessionBackend, IDisposable
{
    public string Session { get; } = fallback.Session;

    public static SdkSessionBackend Create(string name, string dataDirectory, ISessionBackend fallback)
    {
        Directory.CreateDirectory(dataDirectory);
        var s = new Session(new SessionSettings(name, dataDirectory));
        try
        {
            s.Start();
            return new SdkSessionBackend(s, fallback);
        }
        catch (Exception startupError)
        {
            try
            {
                s.Dispose();
            }
            catch (Exception cleanupError)
            {
                startupError.Data["SessionDisposeException"] = cleanupError;
            }

            throw;
        }
    }

    public void Dispose()
    {
        ReleaseWatchers();
        session.Dispose();
    }

    /// <summary>Raised after this backend changes a container and when a watched init process exits on its own.</summary>
    public event Action? ContainersChanged;

    private readonly Dictionary<string, Container> _watched = new(StringComparer.Ordinal);
    private bool _released;

    /// <summary>Drops the container handles kept open to watch for exits. Safe to call more than once.</summary>
    public void ReleaseWatchers()
    {
        List<Container> held;
        lock (_watched)
        {
            _released = true;
            held = [.. _watched.Values];
            _watched.Clear();
        }

        foreach (var c in held)
        {
            c.Dispose();
        }
    }

    // The init process only signals Exited while its container handle stays open, so a started container is held until it exits.
    private void Watch(string name, Container container)
    {
        try
        {
            var init = container.InitProcess;
            if (init is null)
            {
                container.Dispose();
                return;
            }

            lock (_watched)
            {
                if (_released)
                {
                    container.Dispose();
                    return;
                }

                if (_watched.Remove(name, out var previous))
                {
                    previous.Dispose();
                }

                _watched[name] = container;
            }

            init.Exited += exitCode =>
            {
                lock (_watched)
                {
                    if (!_watched.TryGetValue(name, out var current) || !ReferenceEquals(current, container))
                    {
                        return;
                    }

                    _watched.Remove(name);
                }

                // Disposing inside the callback that the handle itself raised could block it.
                _ = Task.Run(() =>
                {
                    container.Dispose();
                    ContainersChanged?.Invoke();
                });
            };
        }
        catch (Exception)
        {
            // Not watchable; the monitor's slow heartbeat still picks the exit up.
            container.Dispose();
        }
    }

    private Task Run(Action<Session> work) => Task.Run(() => work(session));

    private Task WithContainer(string name, Action<Container> action) => Run(s =>
    {
        using (var c = s.OpenContainer(name, ProcessOutputMode.Discard))
        {
            action(c);
        }

        ContainersChanged?.Invoke();
    });

    // Native operations

    public async Task PullImageAsync(string uri, IProgress<PullProgress>? progress)
    {
        var op = session.PullImageAsync(new PullImageOptions(uri));
        var tracker = new PullProgressTracker();
        op.Progress = (_, p) =>
        {
            if (progress is not null)
            {
                lock (tracker)
                {
                    progress.Report(tracker.Update(new PullProgress(p.Status.ToString(), p.Id, (long)p.CurrentBytes, (long)p.TotalBytes)));
                }
            }
        };
        await op;
    }

    public Task DeleteImageAsync(string target) => Run(s => s.DeleteImage(target));

    public Task CreateContainerAsync(string name, string image, string[] commandLine, IReadOnlyDictionary<string, string> environment) => Run(s =>
    {
        var init = new ProcessSettings { OutputMode = ProcessOutputMode.Discard };
        if (commandLine.Length > 0)
        {
            init.CommandLine = commandLine;
        }

        if (environment.Count > 0)
        {
            init.EnvironmentVariables = environment.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        }

        using (var c = s.CreateContainer(new ContainerSettings(image) { Name = name, InitProcess = init }))
        {
        }

        ContainersChanged?.Invoke();
    });

    public Task StartContainerAsync(string name) => Run(s =>
    {
        var c = s.OpenContainer(name, ProcessOutputMode.Discard);
        try
        {
            c.Start();
        }
        catch (Exception) when (IsRunning(s, name))
        {
            // Containers made with `create` can start but fail to attach the init process handle.
        }
        catch
        {
            c.Dispose();
            throw;
        }

        Watch(name, c);
        ContainersChanged?.Invoke();
    });

    public Task StopContainerAsync(string name) => WithContainer(name, c => c.Stop(Signal.SIGTERM, TimeSpan.FromSeconds(10)));

    public Task DeleteContainerAsync(string name) => WithContainer(name, c =>
        c.Delete(c.State == ContainerState.Running ? DeleteContainerOption.Force : DeleteContainerOption.None));

    public Task<string> InspectContainerAsync(string name) => Task.Run(() =>
    {
        using var c = session.OpenContainer(name, ProcessOutputMode.Discard);
        return c.Inspect();
    });

    private static bool IsRunning(Session s, string name)
    {
        try
        {
            using var c = s.OpenContainer(name, ProcessOutputMode.Discard);
            return c.State == ContainerState.Running;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Networks (SDK 3.0.5+)

    private const int RuntimeUpdateNeeded = unchecked((int)0x80040615);
    private const int SdkUpdateNeeded = unchecked((int)0x8004060B);
    private const int NotImplemented = unchecked((int)0x80004001);

    /// <summary>The installed runtime or native SDK can't do this natively, so wslc.exe should.</summary>
    private static bool IsUnsupported(Exception e) =>
        e is EntryPointNotFoundException or NotImplementedException
        || e.HResult is RuntimeUpdateNeeded or SdkUpdateNeeded or NotImplemented;

    private static async Task<T> OrFallback<T>(Func<Task<T>> native, Func<Task<T>> fallback)
    {
        try
        {
            return await native();
        }
        catch (Exception e) when (IsUnsupported(e))
        {
            return await fallback();
        }
    }

    public Task<IReadOnlyList<ResourceRow>> ListResourcesAsync(ResourceKind kind) =>
        kind == ResourceKind.Volume
            ? fallback.ListResourcesAsync(kind)
            : OrFallback(ListNetworksAsync, () => fallback.ListResourcesAsync(kind));

    public Task<IReadOnlyList<ResourceRow>> ListResourceRowsAsync(ResourceKind kind) =>
        kind == ResourceKind.Volume
            ? fallback.ListResourceRowsAsync(kind)
            : OrFallback(() => NetworkRowsAsync(null), () => fallback.ListResourceRowsAsync(kind));

    /// <summary>Inspects in-process: no wslc launch per container.</summary>
    public Task<IReadOnlyDictionary<string, string>> InspectContainersAsync(IReadOnlyList<string> names) => Task.Run(() =>
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            try
            {
                using var c = session.OpenContainer(name, ProcessOutputMode.Discard);
                result[name] = c.Inspect();
            }
            catch (Exception)
            {
                // Removed since it was listed.
            }
        }

        return (IReadOnlyDictionary<string, string>)result;
    });

    private async Task<IReadOnlyList<ResourceRow>> ListNetworksAsync() => await NetworkRowsAsync(await UsageAsync());

    private async Task<IReadOnlyList<ResourceRow>> NetworkRowsAsync(ResourceUsage? usage)
    {
        var networks = await Task.Run(() => session.GetNetworks());
        return networks.Select(n => new ResourceRow(
            n.Name,
            n.Driver,
            // WSLc networks are always local to their session; the SDK doesn't report a scope.
            "local",
            (n.Flags & NetworkFlags.Internal) != 0 ? UiStrings.Get("Internal") : UiStrings.Get("External"),
            !ResourceUsage.BuiltInNetworks.Contains(n.Name),
            usage?.For(ResourceKind.Network, n.Name) ?? [])).ToList();
    }

    public Task<IReadOnlyList<string>> ListNetworkNamesAsync() =>
        OrFallback<IReadOnlyList<string>>(
            async () => (await Task.Run(() => session.GetNetworks())).Select(n => n.Name).ToList(),
            fallback.ListNetworkNamesAsync);

    public Task CreateNetworkAsync(string name, string driver, string subnet, string gateway, bool isInternal)
    {
        var settings = NativeNetworkSettings(name, driver, subnet, gateway, isInternal);
        return settings is null
            ? fallback.CreateNetworkAsync(name, driver, subnet, gateway, isInternal)
            : OrFallback(
                async () => { await Run(s => s.CreateNetwork(settings)); return true; },
                async () => { await fallback.CreateNetworkAsync(name, driver, subnet, gateway, isInternal); return true; });
    }

    public Task RemoveResourceAsync(ResourceKind kind, string name) =>
        kind == ResourceKind.Volume
            ? fallback.RemoveResourceAsync(kind, name)
            : OrFallback(
                async () => { await Run(s => s.DeleteNetwork(name)); return true; },
                async () => { await fallback.RemoveResourceAsync(kind, name); return true; });

    /// <summary>
    /// Settings for a native create, or null when the request needs wslc.exe: the SDK only makes bridge networks and
    /// takes an IPv4 subnet (IPv6 needs its own flags and subnet).
    /// </summary>
    internal static NetworkSettings? NativeNetworkSettings(string name, string driver, string subnet, string gateway, bool isInternal)
    {
        if (driver.Length > 0 && !driver.Equals("bridge", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var settings = new NetworkSettings(name)
        {
            Flags = NetworkFlags.EnableIPv4 | (isInternal ? NetworkFlags.Internal : NetworkFlags.None),
        };
        if (subnet.Length > 0)
        {
            var slash = subnet.IndexOf('/');
            if (slash <= 0
                || !byte.TryParse(subnet[(slash + 1)..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var prefix)
                || prefix > 32
                || !IPAddress.TryParse(subnet[..slash], out var network)
                || network.AddressFamily != AddressFamily.InterNetwork)
            {
                return null;
            }

            settings.Subnet = new HostName(network.ToString());
            settings.SubnetPrefixLength = prefix;
        }

        if (gateway.Length > 0)
        {
            if (subnet.Length == 0 || !IPAddress.TryParse(gateway, out var gw) || gw.AddressFamily != AddressFamily.InterNetwork)
            {
                return null;
            }

            settings.Gateway = new HostName(gw.ToString());
        }

        return settings;
    }

    // Container usage: the SDK can't list containers, but inspecting them is in-process, so only the listing is a wslc call.
    private async Task<ResourceUsage> UsageAsync()
    {
        var containers = await fallback.ListContainersAsync();
        return await Task.Run(() => ResourceUsage.From(containers.SelectMany(c => InspectQuietly(c.Name))));
    }

    private List<(string, JsonElement)> InspectQuietly(string name)
    {
        string json;
        try
        {
            using var c = session.OpenContainer(name, ProcessOutputMode.Discard);
            json = c.Inspect();
        }
        catch (Exception)
        {
            // Removed since the listing.
            return [];
        }

        return ResourceUsage.Objects(json).Take(1).Select(o => (name, o)).ToList();
    }

    // Everything else goes through wslc.exe

    public Task<IReadOnlyList<ContainerRow>> ListContainersAsync() => fallback.ListContainersAsync();

    public Task StreamLogsAsync(string container, LogOptions options, Action<string> onLine, CancellationToken ct) =>
        fallback.StreamLogsAsync(container, options, onLine, ct);

    public IShellProcess StartShell(string container, string shell, Action<string> onOutput, Action<int> onExit) =>
        fallback.StartShell(container, shell, onOutput, onExit);

    public Task CreateContainerAsync(NewContainerSpec spec) => fallback.CreateContainerAsync(spec);

    public Task<IReadOnlyList<ImageSummary>> ListImagesAsync() => fallback.ListImagesAsync();

    public Task<string> InspectResourceAsync(ResourceKind kind, string name) => fallback.InspectResourceAsync(kind, name);

    public Task CreateVolumeAsync(string name, string driver, long? sizeBytes) => fallback.CreateVolumeAsync(name, driver, sizeBytes);

    public Task PruneResourcesAsync(ResourceKind kind) => fallback.PruneResourcesAsync(kind);

    public Task ConnectNetworkAsync(string network, string container, string alias) => fallback.ConnectNetworkAsync(network, container, alias);

    public Task DisconnectNetworkAsync(string network, string container) => fallback.DisconnectNetworkAsync(network, container);

    public Task RegistryLoginAsync(string server, string username, string password) => fallback.RegistryLoginAsync(server, username, password);

    public Task RegistryLogoutAsync(string server) => fallback.RegistryLogoutAsync(server);
}
