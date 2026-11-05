using System.ComponentModel;
using Microsoft.UI.Xaml;
using WslcDesktop.Sessions;

namespace WslcDesktop.ViewModels;

/// <summary>A session row that updates in place.</summary>
public sealed class SessionItem(SessionInfo info) : INotifyPropertyChanged
{
    private SessionInfo _i = info;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id => _i.Id;

    public string Name => _i.Name;

    public string CreatorText => _i.CreatorText;

    public string Containers => _i.Containers;

    public string Images => _i.Images;

    public bool CanTerminate => _i.CanTerminate;

    public Visibility ThisAppVisibility => _i.IsThisApp ? Visibility.Visible : Visibility.Collapsed;

    public void Update(SessionInfo next)
    {
        if (next == _i)
        {
            return;
        }

        _i = next;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}
