namespace WslcDesktop.Data;

/// <summary>Runs an action once, on the first dispose.</summary>
internal sealed class ReleaseHandle(Action release) : IDisposable
{
    private int _done;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _done, 1) == 0)
        {
            release();
        }
    }
}
