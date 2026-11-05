using System.Diagnostics.CodeAnalysis;

namespace WslcDesktop.Restart.Persistence;

public interface IRestartStore
{
    bool TryGet(string name, [NotNullWhen(true)] out RestartEntry? entry);

    IReadOnlyDictionary<string, RestartEntry> Snapshot();

    void Set(string name, RestartEntry? entry);
}