using System.ComponentModel;
using Microsoft.UI.Xaml;
using WslcDesktop.Compose;

namespace WslcDesktop.Services;

/// <summary>A compose stack (containers sharing a project label) that updates in place as containers change.</summary>
public sealed class StackItem(string name) : INotifyPropertyChanged
{
    private int _total;
    private int _running;
    private string _services = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; } = name;

    public string ServicesText => _services;

    public string Summary => $"{_running} / {_total} running";

    public bool AnyRunning => _running > 0;

    public bool AnyStopped => _running < _total;

    public Visibility AllRunningVisibility => _total > 0 && _running == _total ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PartialVisibility => _running > 0 && _running < _total ? Visibility.Visible : Visibility.Collapsed;

    public Visibility StoppedVisibility => _running == 0 ? Visibility.Visible : Visibility.Collapsed;

    public static IReadOnlyList<(string Project, List<ContainerRow> Rows)> Group(IEnumerable<ContainerRow> rows) =>
        rows.Select(r => (Ok: ComposeLabels.TryParse(r.Labels, out var p, out _), Project: p, Row: r))
            .Where(x => x.Ok)
            .GroupBy(x => x.Project, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (g.Key, g.Select(x => x.Row).OrderBy(r => r.Name, StringComparer.Ordinal).ToList()))
            .ToList();

    public void Update(IReadOnlyList<ContainerRow> rows)
    {
        var running = rows.Count(r => ContainerItem.IsRunningState(r.State));
        var services = string.Join(", ", rows.Select(r => ComposeLabels.TryParse(r.Labels, out _, out var s) && s.Length > 0 ? s : r.Name));
        if (running == _running && rows.Count == _total && services == _services)
        {
            return;
        }

        _running = running;
        _total = rows.Count;
        _services = services;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}
