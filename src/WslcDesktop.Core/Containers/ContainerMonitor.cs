namespace WslcDesktop.Containers;

/// <summary>
/// Keeps the container list current in the background so views update without a manual refresh.
/// </summary>
public sealed class ContainerMonitor
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Func<CancellationToken, Task<IReadOnlyList<ContainerRow>>>? _source;
    private CancellationTokenSource? _cts;
    private volatile bool _stopped;

    private PeriodicTimer? _timer;

    public event Action? Updated;

    public TimeSpan Period { get; private set; } = Interval;

    public void SetPeriod(TimeSpan period)
    {
        Period = period;
        try
        {
            if (_timer is { } t)
            {
                t.Period = period;
            }
        }
        catch (ObjectDisposedException)
        {
            // The loop just ended.
        }
    }
    public IReadOnlyList<ContainerRow> Containers { get; private set; } = Array.Empty<ContainerRow>();

    public bool HasLoaded { get; private set; }

    public string? LastError { get; private set; }

    public ContainerRow? Find(string name) =>
        Containers.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal));

    public void Start(Func<CancellationToken, Task<IReadOnlyList<ContainerRow>>> source)
    {
        if (_cts is not null)
        {
            return;
        }

        _stopped = false;
        _source = source;
        _cts = new CancellationTokenSource();
        _ = LoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _stopped = true;
        _cts?.Cancel();
        _cts = null;
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var source = _source;
        var lifetime = _cts?.Token ?? CancellationToken.None;
        if (source is null)
        {
            return;
        }

        // Stop() cancels both the wait for the gate and any wslc call already in flight.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime);
        try
        {
            await _gate.WaitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            Containers = await source(linked.Token);
            HasLoaded = true;
            LastError = null;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception e)
        {
            LastError = e.Message;
        }
        finally
        {
            _gate.Release();
        }

        if (!_stopped)
        {
            Updated?.Invoke();
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Period);
        _timer = timer;
        do
        {
            await RefreshAsync(ct);
        }
        while (await WaitAsync(timer, ct));
    }
    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
