using WslcDesktop.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WslcDesktop.Controls;
using WslcDesktop.Settings;
using WslcDesktop.Terminal;

namespace WslcDesktop.Views.Settings.Tabs.Cards;

/// <summary>Font and size for the Shell tab's terminal, with a live preview. Changes apply to open terminals immediately.</summary>
public static class TerminalCard
{
    public static Border Create(AppSettings settings, StatusReporter report)
    {
        var card = InspectUi.Card(UiStrings.Get("Terminal"), out var rows);
        rows.Spacing = 12;
        rows.Children.Add(InspectUi.Description(UiStrings.Get("FontForTheShellTabSTerminalChangesApply")));

        var fonts = TerminalProtocol.AvailableFonts(InstalledFontNames(), settings.TerminalFontFamily);
        var font = new ComboBox { Header = UiStrings.Get("Font"), ItemsSource = fonts, HorizontalAlignment = HorizontalAlignment.Stretch };
        var size = new NumberBox
        {
            Header = UiStrings.Get("LabelSize"),
            Minimum = TerminalProtocol.MinFontSize,
            Maximum = TerminalProtocol.MaxFontSize,
            SmallChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 160,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(font, UiStrings.Get("TerminalFont"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(size, UiStrings.Get("TerminalFontSize"));

        var preview = new TextBlock
        {
            Text = "user@wslc:~$ ls --color\nbin  dev  etc  home  usr  var\n0O 1lI {}[]() ->  =>  !=",
            Padding = new Thickness(12),
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xCC, 0xCC, 0xCC)),
        };
        var previewBox = new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x0C, 0x0C, 0x0C)),
            CornerRadius = new CornerRadius(4),
            Child = preview,
        };
        var reset = new Button { Content = UiStrings.Get("ResetToDefaults"), HorizontalAlignment = HorizontalAlignment.Left };

        var syncing = false;

        void Show()
        {
            syncing = true;
            if (!fonts.Contains(settings.TerminalFontFamily, StringComparer.OrdinalIgnoreCase))
            {
                fonts.Insert(0, settings.TerminalFontFamily);
                font.ItemsSource = null;
                font.ItemsSource = fonts;
            }

            font.SelectedItem = fonts.First(f => f.Equals(settings.TerminalFontFamily, StringComparison.OrdinalIgnoreCase));
            size.Value = settings.TerminalFontSize;
            preview.FontFamily = new FontFamily($"{settings.TerminalFontFamily}, Consolas");
            preview.FontSize = settings.TerminalFontSize;
            syncing = false;
        }

        void Commit()
        {
            try
            {
                settings.Save();
                settings.TerminalChanged?.Invoke();
            }
            catch (Exception ex)
            {
                report(UiStrings.Get("CouldnTSaveTerminalSettings"), ex.Message, InfoBarSeverity.Error);
            }

            Show();
        }

        font.SelectionChanged += (_, _) =>
        {
            if (!syncing && font.SelectedItem is string f)
            {
                settings.TerminalFontFamily = TerminalProtocol.NormalizeFont(f);
                Commit();
            }
        };
        size.ValueChanged += (_, args) =>
        {
            if (syncing)
            {
                return;
            }

            if (double.IsNaN(args.NewValue))
            {
                Show();
                return;
            }

            settings.TerminalFontSize = TerminalProtocol.ClampFontSize((int)Math.Round(args.NewValue));
            Commit();
        };
        reset.Click += (_, _) =>
        {
            settings.ResetTerminal();
            Commit();
        };

        rows.Children.Add(font);
        rows.Children.Add(size);
        rows.Children.Add(previewBox);
        rows.Children.Add(reset);
        Show();
        return card;
    }

    private static List<string> InstalledFontNames()
    {
        var names = new List<string>();
        foreach (var hive in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
        {
            try
            {
                using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts");
                if (key is not null)
                {
                    names.AddRange(key.GetValueNames());
                }
            }
            catch (Exception)
            {
                // An unreadable hive just means fewer fonts are offered.
            }
        }

        return names;
    }
}
