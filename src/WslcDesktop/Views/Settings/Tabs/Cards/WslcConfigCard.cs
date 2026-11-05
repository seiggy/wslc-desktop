using WslcDesktop.Localization;
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
        var card = InspectUi.Card(UiStrings.Get("WSLcConfiguration"), out var rows);
        rows.Spacing = 12;
        rows.Children.Add(InspectUi.Description(
            UiStrings.Get("SettingsYamlConfiguresWslcSDefaultSessionTheOne")));
        rows.Children.Add(InspectUi.Mono(editor.File.Path));

        try
        {
            editor.Load();
        }
        catch (Exception e)
        {
            report(UiStrings.Get("CouldnTReadSettingsYaml"), e.Message, InfoBarSeverity.Error);
        }

        var fileState = InspectUi.Description(editor.FileExists ? string.Empty : UiStrings.Get("TheFileDoesnTExistYetSavingCreatesIt"));
        rows.Children.Add(fileState);

        var inputs = new Dictionary<string, FrameworkElement>();
        var errors = new Dictionary<string, TextBlock>();
        var save = new Button { Content = UiStrings.Get("ActionSave"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
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
                    report(UiStrings.Get("Saved"), UiStrings.Get("NewSessionsUseTheseSettingsExistingSessionsKeepTheir"), InfoBarSeverity.Success);
                }
            }
            catch (Exception ex)
            {
                report(UiStrings.Get("CouldnTSaveSettingsYaml"), ex.Message, InfoBarSeverity.Error);
            }
        };

        var revert = new Button { Content = UiStrings.Get("ActionRevert") };
        revert.Click += (_, _) =>
        {
            try
            {
                editor.Revert();
            }
            catch (Exception ex)
            {
                report(UiStrings.Get("CouldnTReadSettingsYaml"), ex.Message, InfoBarSeverity.Error);
            }

            Show();
        };

        var open = new Button { Content = UiStrings.Get("OpenFile") };
        open.Click += (_, _) => OpenFile(editor, report);

        var reset = new Button { Content = UiStrings.Get("ResetToDefaults2") };
        reset.Click += async (_, _) =>
        {
            var confirm = new ContentDialog
            {
                XamlRoot = card.XamlRoot,
                Title = UiStrings.Get("ResetWSLcConfiguration"),
                Content = UiStrings.Get("EverySettingGoesBackToDefaultCommentsInThe"),
                PrimaryButtonText = UiStrings.Get("ActionReset"),
                CloseButtonText = UiStrings.Get("ActionCancel"),
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
                report(UiStrings.Get("ActionReset"), UiStrings.Get("AllSettingsUseTheirDefaults"), InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                report(UiStrings.Get("CouldnTResetSettingsYaml"), ex.Message, InfoBarSeverity.Error);
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
            report(UiStrings.Get("CouldnTOpenSettingsYaml"), ex.Message, InfoBarSeverity.Error);
        }
    }
}
