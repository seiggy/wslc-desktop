
using WslcDesktop.Compose.Secrets.Injection;

namespace WslcDesktop.Secrets;

/// <summary>Validated access to the secret store plus the redactor built from its current values.</summary>
public sealed class SecretService(ISecretStore store, TimeSpan? redactorLifetime = null)
{
    private readonly object _gate = new();
    private readonly TimeSpan _lifetime = redactorLifetime ?? TimeSpan.FromSeconds(15);
    private SecretRedactor? _redactor;
    private DateTime _builtAt;

    public static SecretService Default { get; } = new(new CredentialManagerSecretStore());

    /// <summary>Raised after a secret is added, changed or removed from this app.</summary>
    public event Action? Changed;

    public IReadOnlyList<SecretInfo> List() => store.List();

    public bool Exists(string name) => store.List().Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    public string? Get(string name) => store.Get(name);

    public void Save(string name, string value, string label)
    {
        if (!SecretTokens.IsValidName(name))
        {
            throw new ArgumentException($"Names use letters, digits, '.', '_' and '-' (up to {SecretTokens.MaxNameLength} characters) and start with a letter or digit.", nameof(name));
        }

        if (value.Length == 0)
        {
            throw new ArgumentException("The value can't be empty.", nameof(value));
        }

        store.Set(name, value, label.Trim());
        Invalidate();
    }

    public bool Delete(string name)
    {
        var removed = store.Delete(name);
        Invalidate();
        return removed;
    }

    /// <summary>Replaces any stored secret value in <paramref name="text"/> with its <c>${secret:NAME}</c> token (best effort).</summary>
    public string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        return GetRedactor().Redact(text);
    }

    public SecretRedactor GetRedactor()
    {
        lock (_gate)
        {
            if (_redactor is null || DateTime.UtcNow - _builtAt > _lifetime)
            {
                try
                {
                    var values = new List<KeyValuePair<string, string>>();
                    foreach (var info in store.List())
                    {
                        if (store.Get(info.Name) is { } value)
                        {
                            values.Add(new(info.Name, value));
                        }
                    }

                    _redactor = new SecretRedactor(values);
                }
                catch (InvalidOperationException)
                {
                    _redactor ??= SecretRedactor.Empty;
                }

                _builtAt = DateTime.UtcNow;
            }

            return _redactor;
        }
    }

    private void Invalidate()
    {
        lock (_gate)
        {
            _redactor = null;
        }

        Changed?.Invoke();
    }
}
