using WslcDesktop.Services;

namespace WslcDesktop.Tests;

public class SecretServiceTests
{
    private sealed class FakeStore : ISecretStore
    {
        public Dictionary<string, (string Value, string Label)> Data { get; } = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<SecretInfo> List() => Data.Select(k => new SecretInfo(k.Key, k.Value.Label, DateTimeOffset.UnixEpoch)).ToList();

        public string? Get(string name) => Data.TryGetValue(name, out var v) ? v.Value : null;

        public void Set(string name, string value, string label) => Data[name] = (value, label);

        public bool Delete(string name) => Data.Remove(name);
    }

    [Fact]
    public void Save_ValidatesNameAndValue()
    {
        var svc = new SecretService(new FakeStore());
        Assert.ThrowsAny<Exception>(() => svc.Save("bad name", "value1234", ""));
        Assert.ThrowsAny<Exception>(() => svc.Save("ok", "", ""));
        svc.Save("ok", "value1234", "lbl");
        Assert.True(svc.Exists("OK"));
        Assert.Equal("value1234", svc.Get("ok"));
    }

    [Fact]
    public void Redact_UsesStoredValues_AndRefreshesAfterChanges()
    {
        var svc = new SecretService(new FakeStore());
        Assert.Equal("pw=hunter22", svc.Redact("pw=hunter22"));
        svc.Save("db", "hunter22", "");
        Assert.Equal("pw=${secret:db}", svc.Redact("pw=hunter22"));
        svc.Delete("db");
        Assert.Equal("pw=hunter22", svc.Redact("pw=hunter22"));
    }

    [Fact]
    public void Redact_FailsOpenToEmptyWhenStoreThrows()
    {
        var svc = new SecretService(new ThrowingStore());
        Assert.Equal("x", svc.Redact("x"));
    }

    private sealed class ThrowingStore : ISecretStore
    {
        public IReadOnlyList<SecretInfo> List() => throw new InvalidOperationException();

        public string? Get(string name) => throw new InvalidOperationException();

        public void Set(string name, string value, string label) => throw new InvalidOperationException();

        public bool Delete(string name) => throw new InvalidOperationException();
    }
}
