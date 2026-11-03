using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using WslcDesktop.Compose;
using WslcDesktop.Services;

namespace WslcDesktop.Views;

public sealed partial class SecretsPage : Page
{
    private static readonly TimeSpan RevealFor = TimeSpan.FromSeconds(15);

    private readonly System.Collections.ObjectModel.ObservableCollection<SecretItem> _items = [];
    private readonly Dictionary<string, Microsoft.UI.Dispatching.DispatcherQueueTimer> _timers = new(StringComparer.OrdinalIgnoreCase);

    public SecretsPage()
    {
        InitializeComponent();
        List.ItemsSource = _items;
        Loaded += (_, _) => Reload();
        Unloaded += (_, _) =>
        {
            foreach (var t in _timers.Values)
            {
                t.Stop();
            }

            _timers.Clear();
            foreach (var item in _items)
            {
                item.Hide();
            }
        };
    }

    private SecretService Secrets => App.Host.Secrets;

    private void Reload()
    {
        try
        {
            var infos = Secrets.List();
            foreach (var gone in _items.Where(i => infos.All(s => !s.Name.Equals(i.Name, StringComparison.OrdinalIgnoreCase))).ToList())
            {
                _items.Remove(gone);
            }

            for (var n = 0; n < infos.Count; n++)
            {
                var item = _items.FirstOrDefault(i => i.Name.Equals(infos[n].Name, StringComparison.OrdinalIgnoreCase));
                if (item is null)
                {
                    _items.Insert(Math.Min(n, _items.Count), new SecretItem(infos[n]));
                }
                else
                {
                    item.Update(infos[n]);
                    var current = _items.IndexOf(item);
                    if (current != n)
                    {
                        _items.Move(current, n);
                    }
                }
            }

            ErrorBar.IsOpen = false;
        }
        catch (Exception e)
        {
            ErrorBar.Title = "Couldn't read secrets";
            ErrorBar.Message = e.Message;
            ErrorBar.IsOpen = true;
        }

        EmptyText.Visibility = _items.Count == 0 && !ErrorBar.IsOpen ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (await SecretDialogs.PromptAsync(XamlRoot, null) is not null)
        {
            Reload();
        }
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (await SecretDialogs.PromptAsync(XamlRoot, NameOf(sender)) is not null)
        {
            var item = _items.FirstOrDefault(i => i.Name == NameOf(sender));
            item?.Hide();
            Reload();
        }
    }

    private void Reveal_Click(object sender, RoutedEventArgs e)
    {
        var name = NameOf(sender);
        var item = _items.FirstOrDefault(i => i.Name == name);
        if (item is null)
        {
            return;
        }

        if (item.IsRevealed)
        {
            Hide(item);
            return;
        }

        try
        {
            item.Reveal(Secrets.Get(name) ?? string.Empty);
        }
        catch (Exception ex)
        {
            ErrorBar.Title = "Couldn't read the secret";
            ErrorBar.Message = ex.Message;
            ErrorBar.IsOpen = true;
            return;
        }

        // Values re-hide on their own so they don't stay on screen.
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = RevealFor;
        timer.IsRepeating = false;
        timer.Tick += (_, _) => Hide(item);
        _timers[name] = timer;
        timer.Start();
    }

    private void Hide(SecretItem item)
    {
        if (_timers.Remove(item.Name, out var t))
        {
            t.Stop();
        }

        item.Hide();
    }

    private void CopyToken_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(SecretTokens.Format(NameOf(sender)));
        Clipboard.SetContent(package);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var name = NameOf(sender);
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Delete \"{name}\"?",
            Content = "The value is removed from Windows Credential Manager. Compose files and containers that reference "
                + $"{SecretTokens.Format(name)} will fail to start until it's added again. This can't be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            Secrets.Delete(name);
        }
        catch (Exception ex)
        {
            ErrorBar.Title = "Couldn't delete the secret";
            ErrorBar.Message = ex.Message;
            ErrorBar.IsOpen = true;
        }

        Reload();
    }

    private static string NameOf(object sender) => (string)((FrameworkElement)sender).Tag;
}

/// <summary>Add/edit prompt shared by the Secrets page.</summary>
public static class SecretDialogs
{
    /// <summary>Returns the saved name, or null if cancelled. Pass an existing name to edit (the value can be left blank to keep it).</summary>
    public static async Task<string?> PromptAsync(XamlRoot root, string? existing)
    {
        var secrets = App.Host.Secrets;
        var editing = existing is not null;
        var current = editing ? secrets.List().FirstOrDefault(s => s.Name == existing) : null;
        var name = new TextBox { Header = "Name", PlaceholderText = "db-password", Text = existing ?? string.Empty, IsEnabled = !editing, MaxLength = SecretTokens.MaxNameLength };
        var label = new TextBox { Header = "Label (optional)", PlaceholderText = "Where it's used, e.g. shop stack database", Text = current?.Label ?? string.Empty, MaxLength = 200 };
        var value = new PasswordBox
        {
            Header = editing ? "New value (leave blank to keep the current one)" : "Value",
            PasswordRevealMode = PasswordRevealMode.Peek,
        };
        var hint = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["SecondaryTextStyle"],
        };
        var error = new InfoBar { Severity = InfoBarSeverity.Error, IsOpen = false, IsClosable = false };
        var panel = new StackPanel { Spacing = 12, MinWidth = 400, Children = { name, label, value, hint, error } };

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = editing ? $"Edit \"{existing}\"" : "Add secret",
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
        };

        void Validate()
        {
            var n = name.Text.Trim();
            var valid = SecretTokens.IsValidName(n);
            hint.Text = valid
                ? $"Use it as {SecretTokens.Format(n)}"
                : "Names use letters, digits, '.', '_' and '-', and start with a letter or digit.";
            dialog.IsPrimaryButtonEnabled = valid && (editing || value.Password.Length > 0);
        }

        name.TextChanged += (_, _) => Validate();
        value.PasswordChanged += (_, _) => Validate();
        Validate();

        string? saved = null;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            var n = name.Text.Trim();
            try
            {
                if (!editing && secrets.Exists(n))
                {
                    throw new InvalidOperationException($"A secret named '{n}' already exists. Edit it instead.");
                }

                var newValue = value.Password.Length > 0 ? value.Password : secrets.Get(n) ?? throw new InvalidOperationException("Enter a value.");
                secrets.Save(n, newValue, label.Text);
                saved = n;
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                error.Message = ex.Message;
                error.IsOpen = true;
            }
        };

        await dialog.ShowAsync();
        return saved;
    }
}
