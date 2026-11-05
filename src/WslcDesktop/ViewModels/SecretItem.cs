using System.ComponentModel;
using Microsoft.UI.Xaml;
using WslcDesktop.Secrets;

namespace WslcDesktop.ViewModels;

/// <summary>A row on the Secrets page. The value is only held in memory while it is revealed.</summary>
public sealed class SecretItem(SecretInfo info) : INotifyPropertyChanged
{
    public const string Mask = "••••••••";

    private SecretInfo _info = info;
    private string? _revealed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name => _info.Name;

    public string Label => _info.Label;

    public Visibility HasLabel => _info.Label.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public string ModifiedText => _info.Modified.ToLocalTime().ToString("g");

    public bool IsRevealed => _revealed is not null;

    public string Display => _revealed ?? Mask;

    public string RevealGlyph => IsRevealed ? "\uE7B4" : "\uE7B3";

    public void Update(SecretInfo next)
    {
        if (next == _info)
        {
            return;
        }

        _info = next;
        Notify();
    }

    public void Reveal(string value)
    {
        _revealed = value.Replace("\r", " ").Replace("\n", " ");
        Notify();
    }

    public void Hide()
    {
        _revealed = null;
        Notify();
    }

    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
