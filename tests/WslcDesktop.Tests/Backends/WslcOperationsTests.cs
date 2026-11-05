using WslcDesktop.Backends;
using WslcDesktop.Containers;
using WslcDesktop.Resources;
using WslcDesktop.Sessions;

namespace WslcDesktop.Tests.Backends;

public class WslcOperationsTests
{
    private static readonly SessionHandle App = new("WslcDesktop", IsThisApp: true);
    private static readonly SessionHandle Lab = new("lab", IsThisApp: false);

    private readonly Dictionary<string, InMemoryBackend> _backends = new();
    private readonly RecordingEffects _effects = new();
    private readonly WslcOperations _ops;

    public WslcOperationsTests()
    {
        var router = new BackendRouter(owned: null, name => _backends[name] = _backends.GetValueOrDefault(name) ?? new InMemoryBackend(name));
        _ops = new WslcOperations(router, _effects, text => text.Replace("hunter22", "***"));
    }

    private InMemoryBackend BackendOf(SessionHandle s) =>
        _backends.TryGetValue(s.Name, out var backend) ? backend : _backends[s.Name] = new InMemoryBackend(s.Name);

    [Fact]
    public async Task Deleting_an_image_runs_in_the_owning_session_then_invalidates_its_image_cache()
    {
        await _ops.Images.DeleteAsync(Lab, "nginx:latest");

        Assert.Equal(["rmi nginx:latest"], BackendOf(Lab).Calls);
        Assert.Equal(["images:lab"], _effects.Log);
    }

    [Fact]
    public async Task A_failed_image_delete_does_not_invalidate_the_cache()
    {
        var router = new BackendRouter(null, name => new InMemoryBackend(name) { FailWith = new InvalidOperationException("boom") });
        var ops = new WslcOperations(router, _effects);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ops.Images.DeleteAsync(Lab, "nginx"));

        Assert.Empty(_effects.Log);
    }

    [Fact]
    public async Task Starting_an_app_container_is_a_user_start_and_routes_to_the_app_backend()
    {
        await _ops.Containers.StartAsync(App, "web");

        Assert.Equal(["start web"], BackendOf(App).Calls);
        Assert.Equal(["user-started:web"], _effects.Log);
    }

    [Fact]
    public async Task Stopping_an_app_container_notifies_a_user_stop()
    {
        await _ops.Containers.StopAsync(App, "web");

        Assert.Equal(["user-stopped:web"], _effects.Log);
    }

    [Fact]
    public async Task Deleting_an_app_container_notifies_stop_then_removal()
    {
        await _ops.Containers.DeleteAsync(App, "web");

        Assert.Equal(["rm web"], BackendOf(App).Calls);
        Assert.Equal(["user-stopped:web", "removed:web"], _effects.Log);
    }

    [Fact]
    public async Task Containers_in_other_sessions_invalidate_that_sessions_container_cache_and_skip_restart_bookkeeping()
    {
        await _ops.Containers.StartAsync(Lab, "db");
        await _ops.Containers.StopAsync(Lab, "db");
        await _ops.Containers.DeleteAsync(Lab, "db");

        Assert.Equal(["start db", "stop db", "rm db"], BackendOf(Lab).Calls);
        Assert.Equal(["containers:lab", "containers:lab", "containers:lab"], _effects.Log);
    }

    [Fact]
    public async Task Streaming_logs_goes_to_the_session_backend_and_redacts_each_line()
    {
        BackendOf(Lab).LogLines.AddRange(["starting", "password=hunter22"]);
        var lines = new List<string>();

        await _ops.Containers.StreamLogsAsync(Lab, "db", new LogOptions(50, true, false), lines.Add, CancellationToken.None);

        Assert.Equal(["logs db tail=50 follow=True ts=False"], BackendOf(Lab).Calls);
        Assert.Equal(["starting", "password=***"], lines);
    }

    [Fact]
    public void Starting_a_shell_goes_to_the_session_backend_and_redacts_its_output()
    {
        var output = new List<string>();

        var shell = _ops.Containers.StartShell(Lab, "db", "/bin/sh", output.Add, _ => { });
        BackendOf(Lab).Shell.Emit("token hunter22");

        Assert.Same(BackendOf(Lab).Shell, shell);
        Assert.Equal(["shell db /bin/sh"], BackendOf(Lab).Calls);
        Assert.Equal(["token ***"], output);
    }

    [Fact]
    public async Task Inspecting_a_container_returns_the_backends_json()
    {
        var json = await _ops.Containers.InspectAsync(Lab, "db");

        Assert.Contains("\"db\"", json);
    }

    [Theory]
    [InlineData(ResourceKind.Volume)]
    [InlineData(ResourceKind.Network)]
    public async Task Removing_and_pruning_resources_invalidate_that_kind_for_that_session(ResourceKind kind)
    {
        await _ops.Resources.RemoveAsync(Lab, kind, "x");
        await _ops.Resources.PruneAsync(Lab, kind);

        Assert.Equal([$"{kind} remove x", $"{kind} prune"], BackendOf(Lab).Calls);
        Assert.Equal([$"resources:{kind}:lab", $"resources:{kind}:lab"], _effects.Log);
    }

    [Fact]
    public async Task Network_names_lists_the_names_in_that_session()
    {
        var backend = new NetworkListingBackend("lab", ["bridge", "back"]);
        var ops = new WslcOperations(new BackendRouter(null, _ => backend), _effects);

        Assert.Equal(["bridge", "back"], await ops.Resources.NetworkNamesAsync(Lab));
    }

    [Fact]
    public async Task Creating_a_volume_or_network_invalidates_that_kind_for_that_session()
    {
        await _ops.Resources.CreateVolumeAsync(Lab, "data", "vhd", 1024);
        await _ops.Resources.CreateNetworkAsync(Lab, "back", "bridge", "", "", false);

        Assert.Equal(["volume create data", "network create back"], BackendOf(Lab).Calls);
        Assert.Equal([$"resources:{ResourceKind.Volume}:lab", $"resources:{ResourceKind.Network}:lab"], _effects.Log);
    }

    [Fact]
    public async Task Connecting_and_disconnecting_a_network_invalidates_networks_and_containers()
    {
        await _ops.Resources.ConnectNetworkAsync(Lab, "back", "web", "");
        await _ops.Resources.DisconnectNetworkAsync(Lab, "back", "web");

        Assert.Equal(
            [$"resources:{ResourceKind.Network}:lab", "containers:lab", $"resources:{ResourceKind.Network}:lab", "containers:lab"],
            _effects.Log);
    }

    [Fact]
    public async Task Registry_login_goes_to_the_session_backend()
    {
        await _ops.Registry.LoginAsync(App, "ghcr.io", "zack", "pw");

        Assert.Equal(["login ghcr.io"], BackendOf(App).Calls);
    }

    private sealed class NetworkListingBackend(string session, string[] names) : ISessionBackend
    {
        private readonly InMemoryBackend _inner = new(session);

        public string Session => _inner.Session;

        public Task<IReadOnlyList<ResourceRow>> ListResourcesAsync(ResourceKind kind) =>
            Task.FromResult<IReadOnlyList<ResourceRow>>(names.Select(n => new ResourceRow(n, "", "", "", true, [])).ToList());

        public Task<IReadOnlyList<WslcDesktop.Containers.ContainerRow>> ListContainersAsync() => _inner.ListContainersAsync();
        public Task CreateContainerAsync(WslcDesktop.Containers.Creation.NewContainerSpec spec) => _inner.CreateContainerAsync(spec);
        public Task CreateContainerAsync(string name, string image, string[] commandLine, IReadOnlyDictionary<string, string> environment) => _inner.CreateContainerAsync(name, image, commandLine, environment);
        public Task StartContainerAsync(string name) => _inner.StartContainerAsync(name);
        public Task StreamLogsAsync(string container, WslcDesktop.Containers.LogOptions options, Action<string> onLine, CancellationToken ct) => _inner.StreamLogsAsync(container, options, onLine, ct);
        public WslcDesktop.Cli.IShellProcess StartShell(string container, string shell, Action<string> onOutput, Action<int> onExit) => _inner.StartShell(container, shell, onOutput, onExit);
        public Task StopContainerAsync(string name) => _inner.StopContainerAsync(name);
        public Task DeleteContainerAsync(string name) => _inner.DeleteContainerAsync(name);
        public Task<string> InspectContainerAsync(string name) => _inner.InspectContainerAsync(name);
        public Task<IReadOnlyList<WslcDesktop.Images.ImageSummary>> ListImagesAsync() => _inner.ListImagesAsync();
        public Task PullImageAsync(string uri, IProgress<WslcDesktop.Images.PullProgress>? progress) => _inner.PullImageAsync(uri, progress);
        public Task DeleteImageAsync(string target) => _inner.DeleteImageAsync(target);
        public Task<string> InspectResourceAsync(ResourceKind kind, string name) => _inner.InspectResourceAsync(kind, name);
        public Task CreateVolumeAsync(string name, string driver, long? sizeBytes) => _inner.CreateVolumeAsync(name, driver, sizeBytes);
        public Task CreateNetworkAsync(string name, string driver, string subnet, string gateway, bool isInternal) => _inner.CreateNetworkAsync(name, driver, subnet, gateway, isInternal);
        public Task RemoveResourceAsync(ResourceKind kind, string name) => _inner.RemoveResourceAsync(kind, name);
        public Task PruneResourcesAsync(ResourceKind kind) => _inner.PruneResourcesAsync(kind);
        public Task ConnectNetworkAsync(string network, string container, string alias) => _inner.ConnectNetworkAsync(network, container, alias);
        public Task DisconnectNetworkAsync(string network, string container) => _inner.DisconnectNetworkAsync(network, container);
        public Task RegistryLoginAsync(string server, string username, string password) => _inner.RegistryLoginAsync(server, username, password);
        public Task RegistryLogoutAsync(string server) => _inner.RegistryLogoutAsync(server);
    }

    private sealed class RecordingEffects : IOperationEffects
    {
        public List<string> Log { get; } = [];

        public void ImagesChanged(string session) => Log.Add($"images:{session}");

        public void ContainersChanged(string session) => Log.Add($"containers:{session}");

        public void ResourcesChanged(ResourceKind kind, string session) => Log.Add($"resources:{kind}:{session}");

        public void UserStarted(string container) => Log.Add($"user-started:{container}");

        public void UserStopped(string container) => Log.Add($"user-stopped:{container}");

        public void Removed(string container) => Log.Add($"removed:{container}");
    }
}
