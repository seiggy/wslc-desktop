namespace WslcDesktop.Sessions;

/// <summary>Identifies a WSLc session and whether it is the one this app owns. Rows carry one so callers never pass a bare session name.</summary>
public sealed record SessionHandle(string Name, bool IsThisApp);
