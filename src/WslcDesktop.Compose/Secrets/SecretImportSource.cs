using WslcDesktop.Localization;
namespace WslcDesktop.Compose.Secrets;

/// <summary>Where a compose top-level secret's value could be imported from (<c>file:</c>, <c>environment:</c> or <c>content:</c>).</summary>
public sealed record SecretImportSource(string? FilePath = null, string? EnvVar = null, string? Content = null)
{
    public string Describe() => FilePath is not null ? Path.GetFileName(FilePath) : EnvVar is not null ? $"${EnvVar}" : UiStrings.Get("TheComposeFile");
}
