using WslcDesktop.Localization;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using WslcDesktop.Resources;
using WslcDesktop.Sessions;

namespace WslcDesktop.ViewModels;

/// <summary>A volume or network row that is updated in place when a poll reports a change.</summary>
public sealed class ResourceItem(SessionEntry<ResourceRow> entry) : INotifyPropertyChanged
{
    private ResourceRow _r = entry.Item;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Session { get; } = entry.Session;

    public bool IsThisApp { get; } = entry.IsThisApp;

    public string Key => $"{Session}|{_r.Name}";

    public Visibility ThisAppVisibility => IsThisApp ? Visibility.Visible : Visibility.Collapsed;

    public string Name => _r.Name;

    public string Col1 => _r.Col1;

    public string Col2 => _r.Col2;

    public string Col3 => _r.Col3;

    public bool CanRemove => _r.CanRemove;

    public string UsedByText => _r.UsedBy.Count switch
    {
        0 => UiStrings.Get("Unused"),
        1 => _r.UsedBy[0],
        _ => string.Join(", ", _r.UsedBy),
    };

    public void Update(ResourceRow next)
    {
        if (next.Name == _r.Name && next.Col1 == _r.Col1 && next.Col2 == _r.Col2 && next.Col3 == _r.Col3
            && next.CanRemove == _r.CanRemove && next.UsedBy.SequenceEqual(_r.UsedBy))
        {
            return;
        }

        _r = next;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}
