
namespace WslcDesktop.Containers;

/// <summary>A container row placed in the cross-session list, with its stack group heading.</summary>
public sealed record ContainerEntry(string Session, bool IsThisApp, ContainerRow Row, string Stack, string Header)
{
    /// <summary>Container names are unique per session only, so the session is part of the identity.</summary>
    public string Key => $"{Session}|{Row.Name}";
}
