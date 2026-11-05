namespace WslcDesktop.Sessions;

/// <summary>One item as seen from the WSLc session that owns it. Stores are per session, so the same name in two sessions is two entries.</summary>
public sealed record SessionEntry<T>(string Session, bool IsThisApp, T Item);
