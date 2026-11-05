using System.Runtime.InteropServices;
using System.Text;

namespace WslcDesktop.Secrets;

/// <summary>Where secret values live. The real implementation is Windows Credential Manager; tests use a fake.</summary>
public interface ISecretStore
{
    IReadOnlyList<SecretInfo> List();

    string? Get(string name);

    void Set(string name, string value, string label);

    bool Delete(string name);
}
