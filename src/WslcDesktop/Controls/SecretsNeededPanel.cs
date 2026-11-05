using WslcDesktop.Localization;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using WslcDesktop.Compose;
using WslcDesktop.Services;
using WslcDesktop.Compose.Secrets;
using WslcDesktop.Secrets;

namespace WslcDesktop.Controls;

/// <summary>
/// Lists the secrets a compose file (or container) needs and lets the user create the missing ones in place.
/// Typed values stay in memory until saved, so cancelling the surrounding dialog stores nothing.
/// </summary>
public sealed class SecretsNeededPanel : UserControl
{
    private readonly SecretService _secrets;
    private readonly Dictionary<string, Row> _rows = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Row> _order = [];
    private readonly TextBlock _summary = new() { VerticalAlignment = VerticalAlignment.Center, Style = (Style)Application.Current.Resources["SecondaryTextStyle"] };
    private readonly Button _saveAll = new() { Content = UiStrings.Get("ActionSaveAll"), HorizontalAlignment = HorizontalAlignment.Right };
    private readonly StackPanel _rowHost = new() { Spacing = 8 };

    public SecretsNeededPanel(SecretService secrets)
    {
        _secrets = secrets;
        var root = new StackPanel { Spacing = 8 };
        Content = root;
        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel { Spacing = 2 };
        title.Children.Add(new TextBlock { Text = UiStrings.Get("SecretsNeeded"), Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
        title.Children.Add(_summary);
        header.Children.Add(title);
        Grid.SetColumn(_saveAll, 1);
        header.Children.Add(_saveAll);
        root.Children.Add(header);
        root.Children.Add(_rowHost);
        _saveAll.Click += (_, _) =>
        {
            Commit();
            StateChanged?.Invoke();
        };
        Visibility = Visibility.Collapsed;
    }

    /// <summary>Raised whenever a row's saved or pending state changes.</summary>
    public event Action? StateChanged;

    /// <summary>Resolves an <c>environment:</c> compose source to a value from the shell or .env.</summary>
    public Func<string, string?> EnvLookup { get; set; } = _ => null;

    /// <summary>The button that stays disabled while a secret is unset; used in the "left unset" explanation.</summary>
    public string ActionName { get; set; } = UiStrings.Get("ActionUp");

    public bool HasNeeds => _order.Count > 0;

    /// <summary>True when every needed secret is stored or has a valid value waiting to be saved.</summary>
    public bool IsSatisfied => _order.All(r => r.Satisfied);

    /// <summary>Names of secrets the user chose to leave unset.</summary>
    public IReadOnlyList<string> Skipped => _order.Where(r => r.Skip.IsChecked == true).Select(r => r.Need.Name).ToArray();

    public void SetNeeds(IReadOnlyList<SecretNeed> needs)
    {
        var next = new List<Row>();
        foreach (var need in needs)
        {
            if (!_rows.TryGetValue(need.Name, out var row))
            {
                row = new Row(need, this);
                _rows[need.Name] = row;
            }
            else
            {
                row.Need = need;
            }

            next.Add(row);
        }

        foreach (var gone in _rows.Keys.Where(k => next.All(r => !string.Equals(r.Need.Name, k, StringComparison.OrdinalIgnoreCase))).ToList())
        {
            _rows.Remove(gone);
        }

        // Only touch the visual tree when the set of rows changed, so typing and re-planning never flicker.
        if (!next.SequenceEqual(_order))
        {
            _order.Clear();
            _order.AddRange(next);
            _rowHost.Children.Clear();
            foreach (var r in next)
            {
                _rowHost.Children.Add(r.Root);
            }
        }

        foreach (var r in _order)
        {
            r.Refresh();
        }

        Visibility = _order.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSummary();
    }

    /// <summary>Saves every pending value. Returns false (and shows the reason on the row) when one fails.</summary>
    public bool Commit()
    {
        var ok = true;
        foreach (var row in _order.Where(r => r.HasPending))
        {
            ok &= row.Save();
        }

        UpdateSummary();
        return ok;
    }

    private void UpdateSummary()
    {
        var ready = _order.Count(r => r.Satisfied);
        _summary.Text = _order.Count == 0
            ? string.Empty
            : ready == _order.Count ? UiStrings.Get("AllSecretsAreReady") : UiStrings.Format("OfReadyEnterTheMissingValuesToContinue", ready, _order.Count);
        _saveAll.Visibility = _order.Any(r => r.HasPending) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Changed()
    {
        UpdateSummary();
        StateChanged?.Invoke();
    }

    private sealed class Row
    {
        private readonly SecretsNeededPanel _owner;
        private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _usedBy = new() { Style = (Style)Application.Current.Resources["SecondaryTextStyle"], TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock _error = new() { Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"], TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        private readonly TextBlock _skipNote = new() { Style = (Style)Application.Current.Resources["SecondaryTextStyle"], TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        private readonly PasswordBox _value = new() { PlaceholderText = UiStrings.Get("LabelValue"), PasswordRevealMode = PasswordRevealMode.Hidden, HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly ToggleButton _reveal = new() { Content = "\uE7B3", FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"], Style = (Style)Application.Current.Resources["DefaultToggleButtonStyle"] };
        private readonly Button _generate = new() { Content = UiStrings.Get("ActionGenerate") };
        private readonly Button _import = new();
        private readonly TextBox _label = new() { PlaceholderText = UiStrings.Get("LabelOptional"), HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly Button _save = new() { Content = UiStrings.Get("ActionSave") };
        private readonly Button _replace = new() { Content = UiStrings.Get("ActionReplace"), HorizontalAlignment = HorizontalAlignment.Left };
        private readonly Button _cancelReplace = new() { Content = UiStrings.Get("KeepStoredValue") };
        private readonly StackPanel _entry = new() { Spacing = 8 };
        private bool _replacing;

        public Row(SecretNeed need, SecretsNeededPanel owner)
        {
            Need = need;
            _owner = owner;
            Skip = new CheckBox { Content = UiStrings.Get("LeaveUnsetForNow"), MinWidth = 0 };

            var name = new TextBlock { Text = need.Name, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], VerticalAlignment = VerticalAlignment.Center };
            var top = new Grid { ColumnSpacing = 8 };
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.Children.Add(name);
            Grid.SetColumn(_status, 1);
            top.Children.Add(_status);

            var valueRow = new Grid { ColumnSpacing = 6 };
            valueRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            valueRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            valueRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            valueRow.Children.Add(_value);
            Grid.SetColumn(_reveal, 1);
            Grid.SetColumn(_generate, 2);
            valueRow.Children.Add(_reveal);
            valueRow.Children.Add(_generate);
            AutomationProperties(_value, UiStrings.Format("ValueForSecret", need.Name));
            AutomationProperties(_reveal, UiStrings.Format("ShowValueFor", need.Name));
            AutomationProperties(_generate, UiStrings.Format("GenerateARandomValueFor", need.Name));
            AutomationProperties(_label, UiStrings.Format("LabelForSecret", need.Name));
            AutomationProperties(_save, UiStrings.Format("SaveSecret", need.Name));
            AutomationProperties(Skip, UiStrings.Format("LeaveUnset", need.Name));
            ToolTipService.SetToolTip(_reveal, UiStrings.Get("ShowOrHideTheValue"));
            ToolTipService.SetToolTip(_generate, UiStrings.Get("FillInARandomCharacterValue"));

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            actions.Children.Add(_save);
            actions.Children.Add(_import);
            actions.Children.Add(_cancelReplace);
            actions.Children.Add(Skip);

            _entry.Children.Add(valueRow);
            _entry.Children.Add(_label);
            _entry.Children.Add(actions);
            _entry.Children.Add(_skipNote);

            var stack = new StackPanel { Spacing = 6 };
            stack.Children.Add(top);
            stack.Children.Add(_usedBy);
            stack.Children.Add(_replace);
            stack.Children.Add(_entry);
            stack.Children.Add(_error);
            Root = new Border
            {
                Padding = new Thickness(12),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                Child = stack,
            };

            _value.PasswordChanged += (_, _) =>
            {
                _error.Visibility = Visibility.Collapsed;
                Refresh();
                _owner.Changed();
            };
            _reveal.Checked += (_, _) => _value.PasswordRevealMode = PasswordRevealMode.Visible;
            _reveal.Unchecked += (_, _) => _value.PasswordRevealMode = PasswordRevealMode.Hidden;
            _generate.Click += (_, _) =>
            {
                _value.Password = SecretGenerator.Create();
                Skip.IsChecked = false;
            };
            _import.Click += (_, _) => ImportValue();
            _save.Click += (_, _) =>
            {
                Save();
                _owner.Changed();
            };
            _replace.Click += (_, _) =>
            {
                _replacing = true;
                Refresh();
                _owner.Changed();
            };
            _cancelReplace.Click += (_, _) =>
            {
                _replacing = false;
                _value.Password = string.Empty;
                _error.Visibility = Visibility.Collapsed;
                Refresh();
                _owner.Changed();
            };
            Skip.Click += (_, _) =>
            {
                Refresh();
                _owner.Changed();
            };
            Refresh();
        }

        public SecretNeed Need { get; set; }

        public Border Root { get; }

        public CheckBox Skip { get; }

        private bool Editing => !Need.Exists || _replacing;

        public bool HasPending => Editing && _value.Password.Length > 0 && Problem() is null;

        public bool Satisfied => (!Editing) || (HasPending && Skip.IsChecked != true);

        private static void AutomationProperties(DependencyObject o, string name) =>
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(o, name);

        private string? Problem()
        {
            var bytes = Encoding.UTF8.GetByteCount(_value.Password);
            return bytes > CredentialManagerSecretStore.MaxValueBytes
                ? UiStrings.Format("TheValueIsBytesTheLimitIs", bytes, CredentialManagerSecretStore.MaxValueBytes)
                : null;
        }

        public void Refresh()
        {
            _usedBy.Text = Need.Services.Count > 0 ? UiStrings.Format("SecretsUsedBy", string.Join(", ", Need.Services)) : UiStrings.Get("UsedByThisContainer");
            var editing = Editing;
            var skipped = editing && Skip.IsChecked == true;
            _replace.Visibility = !editing ? Visibility.Visible : Visibility.Collapsed;
            _entry.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            _cancelReplace.Visibility = Need.Exists && _replacing ? Visibility.Visible : Visibility.Collapsed;
            Skip.Visibility = Need.Exists ? Visibility.Collapsed : Visibility.Visible;
            _label.Visibility = Need.Exists ? Visibility.Collapsed : Visibility.Visible;
            _value.IsEnabled = _generate.IsEnabled = _reveal.IsEnabled = !skipped;
            _skipNote.Text = skipped ? UiStrings.Format("NothingIsSavedForThisSecretAndStaysUnavailable", _owner.ActionName) : string.Empty;
            _skipNote.Visibility = skipped ? Visibility.Visible : Visibility.Collapsed;

            var import = Need.Import;
            _import.Visibility = editing && import is not null ? Visibility.Visible : Visibility.Collapsed;
            if (import is not null)
            {
                var available = import.Content is not null || (import.FilePath is not null && File.Exists(import.FilePath)) || (import.EnvVar is not null && _owner.EnvLookup(import.EnvVar) is not null);
                _import.Content = import.Content is not null ? UiStrings.Get("UseValueFromTheFile") : UiStrings.Format("ImportFrom", import.Describe());
                _import.IsEnabled = available && !skipped;
                ToolTipService.SetToolTip(_import, available ? UiStrings.Get("StoreTheContentAsASecretTheContainerGets") : UiStrings.Get("TheSourceIsnTAvailableEnterAValueInstead"));
            }

            _save.IsEnabled = HasPending;
            var problem = editing ? Problem() : null;
            if (problem is not null)
            {
                _error.Text = problem;
                _error.Visibility = Visibility.Visible;
            }

            if (Need.Exists && !_replacing)
            {
                _status.Text = UiStrings.Get("Stored");
                _status.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
            }
            else if (skipped)
            {
                _status.Text = UiStrings.Get("LeftUnset");
                _status.Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
            }
            else if (HasPending)
            {
                _status.Text = _replacing ? UiStrings.Get("WillReplaceStoredValue") : UiStrings.Get("ReadyToSave");
                _status.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
            }
            else
            {
                _status.Text = UiStrings.Get("Needed");
                _status.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
            }
        }

        public bool Save()
        {
            try
            {
                _owner._secrets.Save(Need.Name, _value.Password, _label.Text);
                _value.Password = string.Empty;
                _reveal.IsChecked = false;
                _replacing = false;
                Need = Need with { Exists = true };
                _error.Visibility = Visibility.Collapsed;
                Refresh();
                return true;
            }
            catch (Exception ex)
            {
                _error.Text = ex.Message;
                _error.Visibility = Visibility.Visible;
                return false;
            }
        }

        private void ImportValue()
        {
            var import = Need.Import!;
            try
            {
                var text = import.Content
                    ?? (import.FilePath is not null ? File.ReadAllText(import.FilePath) : _owner.EnvLookup(import.EnvVar!) ?? throw new InvalidOperationException(UiStrings.Format("IsnTSet", import.EnvVar)));
                _value.Password = text.TrimEnd('\r', '\n');
                Skip.IsChecked = false;
            }
            catch (Exception ex)
            {
                _error.Text = UiStrings.Format("CouldNotImportSecret", ex.Message);
                _error.Visibility = Visibility.Visible;
            }
        }
    }
}
