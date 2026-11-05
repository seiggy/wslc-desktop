using System.Runtime.InteropServices;
using System.Text;

namespace WslcDesktop.Secrets;

/// <summary>A stored secret's non-sensitive metadata. The value is only ever read through <see cref="ISecretStore.Get"/>.</summary>
public sealed record SecretInfo(string Name, string Label, DateTimeOffset Modified);
