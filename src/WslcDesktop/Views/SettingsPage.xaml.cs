using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WslcDesktop.Controls;
using WslcDesktop.Services;

namespace WslcDesktop.Views;

public sealed partial class SettingsPage : Page
{
    private readonly SettingsFile _file = new();
    private readonly Dictionary<string, FrameworkElement> _inputs = [];
    private readonly Dictionary<string, TextBlock> _errors = [];
    private Button _saveYaml = null!;
    private TextBlock _fileState = null!;

    private NumberBox _cpu = null!;
    private NumberBox _memory = null!;
    private ToggleSwitch _gpu = null!;
    private StackPanel _registryList = null!;
    private TextBox _server = null!;
    private TextBox _user = null!;
    private PasswordBox _password = null!;
    private Button _signIn = null!;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Build();
    }

    private void Build()
    {
        Cards.Children.Clear();
        _inputs.Clear();
        _errors.Clear();
        BuildAppearanceCard();
        BuildYamlCard();
        BuildAppPreferencesCard();
        BuildAppSessionCard();
        BuildRegistryCard();
    }

    private void BuildAppearanceCard()
    {
        var settings = App.Host.Settings;
        var card = InspectUi.Card("Appearance", out var rows);
        rows.Spacing = 12;
        rows.Children.Add(InspectUi.Secondary("Changes apply immediately and are saved for the next launch."));

        var theme = new ComboBox { Header = "Theme", HorizontalAlignment = HorizontalAlignment.Stretch };
        theme.Items.Add("Use Windows setting");
        theme.Items.Add("Light");
        theme.Items.Add("Dark");
        theme.SelectedIndex = settings.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
        theme.SelectionChanged += (_, _) =>
        {
            var previous = settings.Theme;
            settings.Theme = theme.SelectedIndex switch { 1 => "Light", 2 => "Dark", _ => "System" };
            if (!SaveAppearance())
            {
                settings.Theme = previous;
                theme.SelectedIndex = previous switch { "Light" => 1, "Dark" => 2, _ => 0 };
            }
        };
        rows.Children.Add(theme);

        var custom = new ToggleSwitch { Header = "Use custom accent color", IsOn = settings.AccentColor is not null };
        var picker = new ColorPicker
        {
            IsAlphaEnabled = false,
            IsMoreButtonVisible = false,
            IsColorSliderVisible = true,
            IsColorChannelTextInputVisible = true,
            IsHexInputVisible = true,
            Color = settings.AccentColor is { } hex
                ? Windows.UI.Color.FromArgb(255, (byte)(AccentPalette.Parse(hex) >> 16), (byte)(AccentPalette.Parse(hex) >> 8), (byte)AccentPalette.Parse(hex))
                : new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent),
        };
        var choose = new Button { Content = "Choose accent color", IsEnabled = custom.IsOn };
        var flyout = new Flyout();
        var apply = new Button { Content = "Apply color", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        flyout.Content = new StackPanel { Spacing = 12, Children = { picker, apply } };
        choose.Flyout = flyout;
        void SetAccent(string? color)
        {
            var previous = settings.AccentColor;
            settings.AccentColor = color;
            if (!SaveAppearance())
            {
                settings.AccentColor = previous;
                custom.IsOn = previous is not null;
            }

            choose.IsEnabled = custom.IsOn;
        }

        custom.Toggled += (_, _) => SetAccent(custom.IsOn ? $"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}" : null);
        apply.Click += (_, _) =>
        {
            SetAccent($"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}");
            flyout.Hide();
        };
        rows.Children.Add(custom);
        rows.Children.Add(InspectUi.Secondary("Off follows the Windows accent. Custom colors are adjusted for readable light and dark controls."));
        rows.Children.Add(choose);
        Cards.Children.Add(card);
    }

    private bool SaveAppearance()
    {
        try
        {
            App.Host.Settings.Save();
            App.Appearance?.Apply();
            return true;
        }
        catch (Exception ex)
        {
            Status("Couldn't save appearance", ex.Message, InfoBarSeverity.Error);
            return false;
        }
    }

    private void BuildAppPreferencesCard()
    {
        var host = App.Host;
        var card = InspectUi.Card("App preferences", out var rows);
        rows.Spacing = 12;
        rows.Children.Add(new TextBlock
        {
            Text = "How WSLc Desktop Manager behaves on your desktop. Changes apply immediately.",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["SecondaryTextStyle"],
        });

        AddPreference(
            rows,
            "Minimize to tray",
            "Minimizing hides the window to the notification area and removes it from the taskbar. Containers keep running; "
                + "click the tray icon to bring the window back.",
            host.Settings.MinimizeToTray,
            on => host.Settings.MinimizeToTray = on);
        AddPreference(
            rows,
            "Close button minimizes to tray",
            "The close button (X) hides the window to the tray instead of quitting. Use Exit in the tray menu to quit the app.",
            host.Settings.CloseMinimizesToTray,
            on => host.Settings.CloseMinimizesToTray = on);
        Cards.Children.Add(card);
    }

    private void AddPreference(StackPanel rows, string header, string help, bool value, Action<bool> set)
    {
        var toggle = new ToggleSwitch { Header = header, IsOn = value };
        toggle.Toggled += (_, _) =>
        {
            set(toggle.IsOn);
            try
            {
                App.Host.Settings.Save();
                App.Tray?.Apply();
            }
            catch (Exception ex)
            {
                Status("Couldn't save app settings", ex.Message, InfoBarSeverity.Error);
            }
        };
        rows.Children.Add(new StackPanel
        {
            Spacing = 4,
            Children =
            {
                toggle,
                new TextBlock { Text = help, TextWrapping = TextWrapping.Wrap, FontSize = 12, Style = (Style)Application.Current.Resources["SecondaryTextStyle"] },
            },
        });
    }

    private void Status(string title, string message, InfoBarSeverity severity)
    {
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }

    private void BuildYamlCard()
    {
        var card = InspectUi.Card("WSLc configuration", out var rows);
        rows.Spacing = 12;
        rows.Children.Add(new TextBlock
        {
            Text = "settings.yaml configures wslc's default session — the one the wslc command line uses — and any new session it creates. "
                + "WSLc Desktop Manager runs its own session (WslcDesktop), which doesn't read this file; see App session below.",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["SecondaryTextStyle"],
        });
        var path = InspectUi.Mono(_file.Path);
        rows.Children.Add(path);

        try
        {
            _file.LoadOrCreateTemplate();
        }
        catch (Exception e)
        {
            Status("Couldn't read settings.yaml", e.Message, InfoBarSeverity.Error);
        }

        _fileState = new TextBlock
        {
            Text = _file.Exists ? string.Empty : "The file doesn't exist yet; saving creates it.",
            Style = (Style)Application.Current.Resources["SecondaryTextStyle"],
        };
        rows.Children.Add(_fileState);

        foreach (var def in SettingsFile.Definitions)
        {
            var value = _file.Get(def);
            FrameworkElement input;
            if (def.Choices is not null)
            {
                var combo = new ComboBox { Header = def.Label, HorizontalAlignment = HorizontalAlignment.Stretch };
                foreach (var c in def.Choices)
                {
                    combo.Items.Add(c);
                }

                combo.Items.Insert(0, SettingsFile.Default);
                combo.SelectedItem = combo.Items.Cast<string>().FirstOrDefault(i => i.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? SettingsFile.Default;
                combo.SelectionChanged += (_, _) => Validate();
                input = combo;
            }
            else
            {
                var box = new TextBox { Header = def.Label, Text = value, PlaceholderText = SettingsFile.Default };
                box.TextChanged += (_, _) => Validate();
                input = box;
            }

            var error = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.Resources["ErrorTextStyle"],
                Visibility = Visibility.Collapsed,
            };
            _inputs[def.Key] = input;
            _errors[def.Key] = error;
            rows.Children.Add(new StackPanel
            {
                Spacing = 4,
                Children = { input, new TextBlock { Text = def.Help, TextWrapping = TextWrapping.Wrap, FontSize = 12, Style = (Style)Application.Current.Resources["SecondaryTextStyle"] }, error },
            });
        }

        _saveYaml = new Button { Content = "Save", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        _saveYaml.Click += SaveYaml_Click;
        var revert = new Button { Content = "Revert" };
        revert.Click += (_, _) => Build();
        var open = new Button { Content = "Open file" };
        open.Click += Open_Click;
        var reset = new Button { Content = "Reset to defaults…" };
        reset.Click += ResetYaml_Click;
        rows.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 4, 0, 0),
            Children = { _saveYaml, revert, open, reset },
        });
        Cards.Children.Add(card);
    }

    private string InputValue(SettingDef def) => _inputs[def.Key] switch
    {
        TextBox t => t.Text,
        ComboBox c => c.SelectedItem as string ?? SettingsFile.Default,
        _ => string.Empty,
    };

    private bool Validate()
    {
        var ok = true;
        foreach (var def in SettingsFile.Definitions)
        {
            var message = SettingsFile.Validate(def, InputValue(def));
            _errors[def.Key].Text = message ?? string.Empty;
            _errors[def.Key].Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
            ok &= message is null;
        }

        _saveYaml.IsEnabled = ok;
        return ok;
    }

    private void SaveYaml_Click(object sender, RoutedEventArgs e)
    {
        if (!Validate())
        {
            return;
        }

        try
        {
            foreach (var def in SettingsFile.Definitions)
            {
                var v = InputValue(def).Trim();
                if (!v.Equals(_file.Get(def), StringComparison.Ordinal) && !(v.Equals(SettingsFile.Default, StringComparison.OrdinalIgnoreCase) && !_file.IsSet(def)))
                {
                    _file.Set(def, v);
                }
            }

            _file.Save();
            Status("Saved", "New sessions use these settings. Existing sessions keep their current ones until they restart.", InfoBarSeverity.Success);
            _fileState.Text = string.Empty;
        }
        catch (Exception ex)
        {
            Status("Couldn't save settings.yaml", ex.Message, InfoBarSeverity.Error);
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_file.Exists)
            {
                _file.Save();
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_file.Path) { UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                System.Diagnostics.Process.Start("notepad.exe", $"\"{_file.Path}\"");
            }
        }
        catch (Exception ex)
        {
            Status("Couldn't open settings.yaml", ex.Message, InfoBarSeverity.Error);
        }
    }

    private async void ResetYaml_Click(object sender, RoutedEventArgs e)
    {
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
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
            _file.Load();
            _file.ResetAll();
            _file.Save();
            Build();
            Status("Reset", "All settings use their defaults.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Status("Couldn't reset settings.yaml", ex.Message, InfoBarSeverity.Error);
        }
    }

    private void BuildAppSessionCard()
    {
        var host = App.Host;
        var card = InspectUi.Card("App session (WslcDesktop)", out var rows);
        rows.Spacing = 12;
        rows.Children.Add(new TextBlock
        {
            Text = "Resources for the session WSLc Desktop Manager runs your containers in. These are passed to WSLc when the app starts, "
                + "so changes apply the next time you open the app.",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["SecondaryTextStyle"],
        });
        InspectUi.AddRow(rows, "Running with", $"{host.StartedCpuCount} CPUs, {host.StartedMemoryMb:N0} MB memory");
        InspectUi.AddRow(rows, "Storage", Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WslcDesktop", "Data"), true);

        _cpu = new NumberBox
        {
            Header = $"CPU count (1–{Environment.ProcessorCount})",
            Minimum = 1,
            Maximum = Environment.ProcessorCount,
            Value = host.Settings.CpuCount,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        _memory = new NumberBox
        {
            Header = "Memory (MB)",
            Minimum = 512,
            Value = host.Settings.MemoryMb,
            SmallChange = 512,
            LargeChange = 1024,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        _gpu = new ToggleSwitch { Header = "GPU access", IsOn = host.Settings.EnableGpu };
        var save = new Button { Content = "Save", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        save.Click += (_, _) =>
        {
            if (double.IsNaN(_cpu.Value) || double.IsNaN(_memory.Value))
            {
                Status("Check the values", "CPU count and memory must be numbers.", InfoBarSeverity.Warning);
                return;
            }

            host.Settings.CpuCount = (uint)Math.Clamp(_cpu.Value, 1, Environment.ProcessorCount);
            host.Settings.MemoryMb = (uint)Math.Max(_memory.Value, 512);
            host.Settings.EnableGpu = _gpu.IsOn;
            try
            {
                host.Settings.Save();
                Status("Saved", "Restart WSLc Desktop Manager to apply the new session resources.", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                Status("Couldn't save app settings", ex.Message, InfoBarSeverity.Error);
            }
        };
        var reset = new Button { Content = "Reset to defaults" };
        reset.Click += (_, _) =>
        {
            _cpu.Value = AppSettings.DefaultCpuCount;
            _memory.Value = AppSettings.DefaultMemoryMb;
            _gpu.IsOn = false;
        };
        rows.Children.Add(_cpu);
        rows.Children.Add(_memory);
        rows.Children.Add(_gpu);
        rows.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, reset } });
        Cards.Children.Add(card);
    }

    private void BuildRegistryCard()
    {
        var card = InspectUi.Card("Registries", out var rows);
        rows.Spacing = 12;
        rows.Children.Add(new TextBlock
        {
            Text = "Sign in to private registries so images can be pulled. Credentials are handed to wslc and kept in its credential store; "
                + "this app never stores or shows them. WSLc can't list saved credentials, so the list below shows registries you signed in to from here.",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["SecondaryTextStyle"],
        });

        _registryList = new StackPanel { Spacing = 4 };
        rows.Children.Add(_registryList);
        RenderRegistries();

        _server = new TextBox { Header = "Server (blank for the default registry)", PlaceholderText = "ghcr.io" };
        _user = new TextBox { Header = "Username" };
        _password = new PasswordBox { Header = "Password or access token" };
        _signIn = new Button { Content = "Sign in", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        _signIn.Click += SignIn_Click;
        rows.Children.Add(_server);
        rows.Children.Add(_user);
        rows.Children.Add(_password);
        rows.Children.Add(_signIn);
        Cards.Children.Add(card);
    }

    private void RenderRegistries()
    {
        _registryList.Children.Clear();
        var list = App.Host.Settings.Registries;
        if (list.Count == 0)
        {
            _registryList.Children.Add(InspectUi.Secondary("No registries signed in from this app."));
            return;
        }

        foreach (var server in list.ToList())
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = server.Length == 0 ? "Default registry" : server, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var out_ = new Button { Content = "Sign out" };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(out_, $"Sign out of {server}");
            out_.Click += async (_, _) => await SignOutAsync(server);
            Grid.SetColumn(out_, 1);
            row.Children.Add(out_);
            _registryList.Children.Add(row);
        }
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        var server = _server.Text.Trim();
        var user = _user.Text.Trim();
        if (user.Length == 0 || _password.Password.Length == 0)
        {
            Status("Missing details", "Enter a username and a password or access token.", InfoBarSeverity.Warning);
            return;
        }

        _signIn.IsEnabled = false;
        try
        {
            await App.Host.RegistryLoginAsync(server, user, _password.Password);
            if (!App.Host.Settings.Registries.Contains(server, StringComparer.OrdinalIgnoreCase))
            {
                App.Host.Settings.Registries.Add(server);
                App.Host.Settings.Save();
            }

            _password.Password = string.Empty;
            RenderRegistries();
            Status("Signed in", server.Length == 0 ? "Signed in to the default registry." : $"Signed in to {server}.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Status("Couldn't sign in", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            _signIn.IsEnabled = true;
        }
    }

    private async Task SignOutAsync(string server)
    {
        try
        {
            await App.Host.RegistryLogoutAsync(server);
            App.Host.Settings.Registries.RemoveAll(s => s.Equals(server, StringComparison.OrdinalIgnoreCase));
            App.Host.Settings.Save();
            RenderRegistries();
            Status("Signed out", server.Length == 0 ? "Signed out of the default registry." : $"Signed out of {server}.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Status("Couldn't sign out", ex.Message, InfoBarSeverity.Error);
        }
    }
}
