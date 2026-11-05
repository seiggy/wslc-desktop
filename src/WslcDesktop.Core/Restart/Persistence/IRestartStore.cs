namespace WslcDesktop.Restart.Persistence;

public interface IRestartStore
{
    IReadOnlyDictionary<string, RestartEntry> Snapshot();

    void Set(string name, RestartEntry? entry);
}