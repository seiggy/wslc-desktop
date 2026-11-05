using System.ComponentModel;
using Microsoft.UI.Xaml;
using WslcDesktop.Images;

namespace WslcDesktop.ViewModels;

/// <summary>An image row whose values are replaced in place when a poll reports a change.</summary>
public sealed class ImageItem(SessionImage row) : INotifyPropertyChanged
{
    private SessionImage _row = row;

    private ImageSummary _s => _row.Image;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key => _row.Key;

    public string Session => _row.Session;

    public bool IsThisApp => _row.IsThisApp;

    public bool CanCopyToApp => _row.CanCopyToApp;

    public Visibility ThisAppVisibility => _row.IsThisApp ? Visibility.Visible : Visibility.Collapsed;

    public string Id => _s.Id;

    public string DisplayName => _s.DisplayName;

    public string ShortId => _s.ShortId;

    public string Target => _s.Target;

    public string RunReference => _s.Target;
    public string TagStatus => _s.TagStatus;

    public string UsedBy => _s.UsedBy;

    public string Size => _s.Size;

    public string CreatedSince => _s.CreatedSince;

    public Visibility TaggedVisibility => _s.IsTagged ? Visibility.Visible : Visibility.Collapsed;

    public Visibility UntaggedVisibility => _s.IsTagged ? Visibility.Collapsed : Visibility.Visible;

    public void Update(SessionImage next)
    {
        if (next == _row)
        {
            return;
        }

        _row = next;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}
