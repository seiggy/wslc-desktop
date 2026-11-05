namespace WslcDesktop.Containers;

/// <summary>How much of a container's log to read and whether to keep following it.</summary>
public sealed record LogOptions(int Tail, bool Follow, bool Timestamps);
