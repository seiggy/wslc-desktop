using System.Text.Json;

namespace WslcDesktop.Containers.Creation;

public sealed record NewContainerSpec(
    string Name,
    string Image,
    string[] Command,
    IReadOnlyList<string> Ports,
    IReadOnlyList<string> Mounts,
    IReadOnlyList<string> Env,
    string Network);
