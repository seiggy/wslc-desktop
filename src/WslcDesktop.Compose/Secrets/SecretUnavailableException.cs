using WslcDesktop.Localization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WslcDesktop.Compose.Secrets;

/// <summary>Raised when a token references a secret that isn't in the store.</summary>
public sealed class SecretUnavailableException(string name)
    : InvalidOperationException(UiStrings.Format("TheSecretIsnTStoredAddItOnThe", name))
{
    public string SecretName { get; } = name;
}
