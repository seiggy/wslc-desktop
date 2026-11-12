namespace WslcDesktop.Data;

/// <summary>The kinds of data the store keeps; pages watch them and listen for their changes.</summary>
public enum DataKind
{
    Sessions,
    Versions,
    Containers,
    Inspect,
    Images,
    Volumes,
    Networks,

    /// <summary>CPU, memory and I/O samples; only read while a page shows them.</summary>
    Stats,
}
