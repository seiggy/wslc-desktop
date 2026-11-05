namespace WslcDesktop.Restart.Monitoring;

public interface IRestartActions
{
    Task StartAsync(string name, CancellationToken ct);
}