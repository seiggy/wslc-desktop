
namespace WslcDesktop.Containers;

/// <summary>Navigation parameter for a container in a specific session.</summary>
public sealed record ContainerRef(string Session, string Name);
