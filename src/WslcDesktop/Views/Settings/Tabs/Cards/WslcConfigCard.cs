using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WslcDesktop.Controls;
using WslcDesktop.Settings;

namespace WslcDesktop.Views.Settings.Tabs.Cards;

/// <summary>Edits wslc's settings.yaml, the configuration of the default session and of any new session wslc creates.</summary>
public static class WslcConfigCard
{
    public static Border Create(StatusReporter report)
    {
        var editor = new WslcConfigEditor(new SettingsFile());
        var card = InspectUi.Card("WSLc configuration", out var rows);
        rows.Spacing = 12;
        rows.Children.Add(InspectUi.Description(
            "settings.yaml configures wslc's default session — the one the wslc command line uses — and any new session it creates. "
            + "WSLc Desktop Manager runs its own session (WslcDesktop), which doesn't read this file; see the App session card above."));
        rows.Children.Add(InspectUi.Mono(editor.File.Path));

        try
        {
            editor.Load();
        }
        catch (Exception e)
        {
            report("Couldn't read settings.yaml", e.Message, InfoBarSeverity.Error);
        }

        var fileState = InspectUi.Description(editor.FileExists ? string.Empty : "The file doesn't exist yet; saving creates it.");
        rows.Children.Add(fileState);

        var inputs = new Dictionary<string, FrameworkElement>();
        var errors = new Dictionary<string, TextBlock>();
        var save = new Button { Content = "Save", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        var syncing = false;

        void Validate()
        {
            foreach (var def in SettingsFile.Definitions)
            {
                var message = editor.ErrorFor(def.Key);
                errors[def.Key].Text = message ?? string.Empty;
                errors[def.Key].Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
            }

            save.IsEnabled = editor.IsValid;
        }

        void Show()
        {
            syncing = true;
            foreach (var def in SettingsFile.Definitions)
            {
                var value = editor.Get(def.Key);
                switch (inputs[def.Key])
                {
                    case TextBox box:
                        box.Text = value;
                        break;
                    case ComboBox combo:
                        combo.SelectedItem = combo.Items.Cast<string>().FirstOrDefault(i => i.Equals(value, StringComparison.OrdinalIgnoreCase))
                            ?? SettingsFile.Default;
                        break;
                }
            }

            syncing = false;
            Validate();
        }

        foreach (var def in SettingsFile.Definitions)
        {
            FrameworkElement input;
            var key = def.Key;
            if (def.Choices is not null)
            {
                var combo = new ComboBox { Header = def.Label, HorizontalAlignment = HorizontalAlignment.Stretch };
                combo.Items.Add(SettingsFile.Default);
                foreach (var c in def.Choices)
                {
                    combo.Items.Add(c);
                }

                combo.SelectionChanged += (_, _) =>
                {
                    if (!syncing)
                    {
                        editor.SetDraft(key, combo.SelectedItem as string ?? SettingsFile.Default);
                        Validate();
                    }
                };
                input = combo;
            }
            else
            {
                var box = new TextBox { Header = def.Label, PlaceholderText = SettingsFile.Default };
                box.TextChanged += (_, _) =>
                {
                    if (!syncing)
                    {
                        editor.SetDraft(key, box.Text);
                        Validate();
                    }
                };
                input = box;
            }

            var error = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.Resources["ErrorTextStyle"],
                Visibility = Visibility.Collapsed,
            };
            inputs[key] = input;
            errors[key] = error;
            var help = InspectUi.Description(def.Help);
            help.FontSize = 12;
            rows.Children.Add(new StackPanel { Spacing = 4, Children = { input, help, error } });
        }

        save.Click += (_, _) =>
        {
            try
            {
                if (editor.Save())
                {
                    fileState.Text = string.Empty;
                    report("Saved", "New sessions use these settings. Existing sessions keep their current ones until they restart.", InfoBarSeverity.Success);
                }
            }
            catch (Exception ex)
            {
                report("Couldn't save settings.yaml", ex.Message, InfoBarSeverity.Error);
            }
        };

        var revert = new Button { Content = "Revert" };
        revert.Click += (_, _) =>
        {
            try
            {
                editor.Revert();
            }
            catch (Exception ex)
            {
                report("Couldn't read settings.yaml", ex.Message, InfoBarSeverity.Error);
            }

            Show();
        };

        var open = new Button { Content = "Open file" };
        open.Click += (_, _) => OpenFile(editor, report);

        var reset = new Button { Content = "Reset to defaults…" };
        reset.Click += async (_, _) =>
        {
            var confirm = new ContentDialog
            {
                XamlRoot = card.XamlRoot,
                Title = "Reset WSLc configuration?",
                Content = "Every setting goes back to \"default\". Comments in the file are kept.",
                PrimaryButtonText = "Reset",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            try
            {
                editor.Reset();
                Show();
                report("Reset", "All settings use their defaults.", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                report("Couldn't reset settings.yaml", ex.Message, InfoBarSeverity.Error);
            }
        };

        rows.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 4, 0, 0),
            Children = { save, revert, open, reset },
        });
        Show();
        return card;
    }

    private static void OpenFile(WslcConfigEditor editor, StatusReporter report)
    {
        try
        {
            if (!editor.FileExists)
            {
                editor.File.Save();
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(editor.File.Path) { UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                System.Diagnostics.Process.Start("notepad.exe", $"\"{editor.File.Path}\"");
            }
        }
        catch (Exception ex)
        {
            report("Couldn't open settings.yaml", ex.Message, InfoBarSeverity.Error);
        }
    }
}
