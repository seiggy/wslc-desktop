using System.Text.Json;

namespace WslcDesktop.Resources;

/// <summary>Navigation parameter for a volume or network in a specific session.</summary>
public sealed record ResourceRef(ResourceKind Kind, string Session, string Name);
