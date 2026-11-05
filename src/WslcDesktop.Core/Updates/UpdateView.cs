namespace WslcDesktop.Updates;

/// <summary>What the Settings card should show for a given update state.</summary>
public sealed record UpdateView(string Title, string Message, bool CanCheck, bool CanDownload, bool CanRestart, bool ShowProgress);
