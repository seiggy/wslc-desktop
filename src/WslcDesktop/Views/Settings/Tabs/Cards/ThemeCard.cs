using WslcDesktop.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WslcDesktop.Controls;
using WslcDesktop.Settings;

namespace WslcDesktop.Views.Settings.Tabs.Cards;

/// <summary>Light, dark or Windows theme, and the accent colour. Changes apply immediately.</summary>
public static class ThemeCard
{
    public static Border Create(AppSettings settings, StatusReporter report)
    {
        var card = InspectUi.Card(UiStrings.Get("ThemeAndColor"), out var rows);
        rows.Spacing = 12;
        rows.Children.Add(InspectUi.Description(UiStrings.Get("ChangesApplyImmediatelyAndAreSavedForTheNext")));

        bool Save()
        {
            try
            {
                settings.Save();
                App.Appearance?.Apply();
                return true;
            }
            catch (Exception ex)
            {
                report(UiStrings.Get("CouldnTSaveAppearance"), ex.Message, InfoBarSeverity.Error);
                return false;
            }
        }

        var theme = new ComboBox { Header = UiStrings.Get("Theme"), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var label in AppearanceChoices.ThemeLabels)
        {
            theme.Items.Add(label);
        }

        theme.SelectedIndex = AppearanceChoices.ThemeIndex(settings.Theme);
        theme.SelectionChanged += (_, _) =>
        {
            var previous = settings.Theme;
            settings.Theme = AppearanceChoices.ThemeFromIndex(theme.SelectedIndex);
            if (!Save())
            {
                settings.Theme = previous;
                theme.SelectedIndex = AppearanceChoices.ThemeIndex(previous);
            }
        };
        rows.Children.Add(theme);

        var custom = new ToggleSwitch { Header = UiStrings.Get("UseCustomAccentColor"), IsOn = settings.AccentColor is not null };
        var picker = new ColorPicker
        {
            IsAlphaEnabled = false,
            IsMoreButtonVisible = false,
            IsColorSliderVisible = true,
            IsColorChannelTextInputVisible = true,
            IsHexInputVisible = true,
            Color = InitialColor(settings),
        };
        var choose = new Button { Content = UiStrings.Get("ChooseAccentColor"), IsEnabled = custom.IsOn };
        var apply = new Button { Content = UiStrings.Get("ApplyColor"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        var flyout = new Flyout { Content = new StackPanel { Spacing = 12, Children = { picker, apply } } };
        choose.Flyout = flyout;

        void SetAccent(string? color)
        {
            var previous = settings.AccentColor;
            settings.AccentColor = color;
            if (!Save())
            {
                settings.AccentColor = previous;
                custom.IsOn = previous is not null;
            }

            choose.IsEnabled = custom.IsOn;
        }

        string Picked() => AccentPalette.ToHex(picker.Color.R, picker.Color.G, picker.Color.B);
        custom.Toggled += (_, _) => SetAccent(custom.IsOn ? Picked() : null);
        apply.Click += (_, _) =>
        {
            SetAccent(Picked());
            flyout.Hide();
        };

        rows.Children.Add(custom);
        rows.Children.Add(InspectUi.Description(UiStrings.Get("OffFollowsTheWindowsAccentCustomColorsAreAdjusted")));
        rows.Children.Add(choose);
        return card;
    }

    private static Windows.UI.Color InitialColor(AppSettings settings)
    {
        if (settings.AccentColor is { } hex)
        {
            var (r, g, b) = AccentPalette.Channels(hex);
            return Windows.UI.Color.FromArgb(255, (byte)r, (byte)g, (byte)b);
        }

        return new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
    }
}
