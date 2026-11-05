using WslcDesktop.Resources;

namespace WslcDesktop.Backends;

/// <summary>What an operation must tell the rest of the app: stale caches and user-driven container changes (so restart policies don't fight the user).</summary>
public interface IOperationEffects
{
    void ImagesChanged(string session);

    void ContainersChanged(string session);

    void ResourcesChanged(ResourceKind kind, string session);

    void UserStarted(string container);

    void UserStopped(string container);

    void Removed(string container);
}
