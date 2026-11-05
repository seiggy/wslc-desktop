using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WslcDesktop.Compose.Secrets;

/// <summary>Raised when a token references a secret that isn't in the store.</summary>
public sealed class SecretUnavailableException(string name)
    : InvalidOperationException($"The secret '{name}' isn't stored. Add it on the Secrets page.")
{
    public string SecretName { get; } = name;
}
