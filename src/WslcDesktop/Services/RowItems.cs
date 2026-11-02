using System.ComponentModel;
using Microsoft.UI.Xaml;

namespace WslcDesktop.Services;

/// <summary>An image row whose values are replaced in place when a poll reports a change.</summary>
public sealed class ImageItem(ImageSummary summary) : INotifyPropertyChanged
{
    private ImageSummary _s = summary;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id => _s.Id;

    public string DisplayName => _s.DisplayName;

    public string ShortId => _s.ShortId;

    public string Target => _s.Target;

    public string TagStatus => _s.TagStatus;

    public string UsedBy => _s.UsedBy;

    public string Size => _s.Size;

    public string CreatedSince => _s.CreatedSince;

    public Visibility TaggedVisibility => _s.TaggedVisibility;

    public Visibility UntaggedVisibility => _s.UntaggedVisibility;

    public void Update(ImageSummary next)
    {
        if (next == _s)
        {
            return;
        }

        _s = next;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}

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

    public Visibility ThisAppVisibility => _i.ThisAppVisibility;

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
