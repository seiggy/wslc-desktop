using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WslcDesktop.Controls;

/// <summary>Builds the labeled card/row layout shared by the inspect views.</summary>
public static class InspectUi
{
    public static Border Card(string title, out StackPanel rows)
    {
        rows = new StackPanel { Spacing = 8 };
        return new Border
        {
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = title, Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] },
                    rows,
                },
            },
        };
    }

    // Empty values are skipped so a card only lists what is actually defined.
    public static void AddRow(StackPanel target, string label, string? value, bool mono = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var row = new Grid { ColumnSpacing = 16 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(Secondary(label));
        var text = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        if (mono)
        {
            text.FontFamily = new FontFamily("Cascadia Mono, Consolas");
            text.FontSize = 12;
        }

        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        target.Children.Add(row);
    }

    public static TextBlock Secondary(string text) => new()
    {
        Text = text,
        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
    };

    public static TextBlock Mono(string text) => new()
    {
        Text = text,
        IsTextSelectionEnabled = true,
        TextWrapping = TextWrapping.Wrap,
        FontFamily = new FontFamily("Cascadia Mono, Consolas"),
        FontSize = 12,
    };

    // The API reports "0001-01-01T00:00:00Z" for "never".
    public static string FormatDate(string? value) =>
        DateTimeOffset.TryParse(value, out var d) && d.Year > 1 ? d.ToLocalTime().ToString("f") : string.Empty;
}
