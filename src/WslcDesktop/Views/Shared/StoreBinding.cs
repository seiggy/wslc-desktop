using Microsoft.UI.Dispatching;
using WslcDesktop.Data;

namespace WslcDesktop.Views.Shared;

/// <summary>
/// Binds a page to the background store: renders straight away from what the store already holds, re-renders on the
/// UI thread when a kind it listens to changes (bursts collapse into one render), and keeps the watched kinds on
/// their faster refresh intervals until disposed.
/// </summary>
public sealed class StoreBinding : IDisposable
{
    private readonly DispatcherQueue _queue;
    private readonly Action _apply;
    private readonly HashSet<DataKind> _listen;
    private readonly IDisposable _watch;
    private int _queued;
    private bool _disposed;

    /// <param name="watch">Kinds this page shows; they refresh faster while it's open.</param>
    /// <param name="alsoListen">Kinds that affect the page without needing faster refreshes.</param>
    public StoreBinding(DispatcherQueue queue, Action apply, DataKind[] watch, params DataKind[] alsoListen)
    {
        _queue = queue;
        _apply = apply;
        _listen = [.. watch, .. alsoListen];
        _watch = App.Host.Store.Watch(watch);
        App.Host.Store.Changed += OnChanged;
        apply();
    }

    private void OnChanged(DataKind kind)
    {
        if (!_listen.Contains(kind) || Interlocked.Exchange(ref _queued, 1) == 1)
        {
            return;
        }

        if (!_queue.TryEnqueue(() =>
        {
            Volatile.Write(ref _queued, 0);
            if (!_disposed)
            {
                _apply();
            }
        }))
        {
            Volatile.Write(ref _queued, 0);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        App.Host.Store.Changed -= OnChanged;
        _watch.Dispose();
    }
}
