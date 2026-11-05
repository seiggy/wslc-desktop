namespace WslcDesktop.Compose.Secrets;

/// <summary>A secret the compose file needs: who uses it, whether it is already stored, and any value the file itself can supply.</summary>
public sealed record SecretNeed(string Name, IReadOnlyList<string> Services, bool Exists, SecretImportSource? Import = null);
