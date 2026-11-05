namespace WslcDesktop.Images;

public sealed record ImageDetail(
    string Id,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Digests,
    string Created,
    string Size,
    string Os,
    string Architecture,
    string Author,
    string Comment,
    string User,
    string WorkingDir,
    IReadOnlyList<string> Entrypoint,
    IReadOnlyList<string> Cmd,
    IReadOnlyList<string> Env,
    IReadOnlyList<string> Ports,
    IReadOnlyList<string> Labels,
    IReadOnlyList<string> Layers,
    string RawJson);
