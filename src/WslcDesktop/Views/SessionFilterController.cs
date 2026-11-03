using Microsoft.UI.Xaml.Controls;

namespace WslcDesktop.Views;

/// <summary>
/// Drives the "All sessions / one session" ComboBox shared by the Images, Containers, Volumes and Networks pages.
/// The item list is patched in place on each refresh so an open dropdown or the selection never flickers.
/// </summary>
public sealed class SessionFilterController
{
    public const string AllSessions = "All sessions";

    private readonly ComboBox _box;
    private IReadOnlyList<string> _sessions = [];
    private bool _updating;

    public SessionFilterController(ComboBox box)
    {
        _box = box;
        _box.SelectionChanged += (_, _) =>
        {
            if (_updating || _box.SelectedIndex < 0)
            {
                return;
            }

            Filter = _box.SelectedIndex == 0 ? null : _sessions[_box.SelectedIndex - 1];
            Changed?.Invoke();
        };
    }

    /// <summary>The selected session name, or null for all sessions.</summary>
    public string? Filter { get; private set; }

    public event Action? Changed;

    public void Sync(IReadOnlyList<string> sessions)
    {
        _sessions = sessions;
        var wanted = new List<string> { AllSessions };
        wanted.AddRange(sessions.Select(s => App.Host.IsAppSession(s) ? $"{s} (this app)" : s));
        var current = _box.Items.Cast<string>().ToList();
        _updating = true;
        try
        {
            if (!current.SequenceEqual(wanted))
            {
                for (var i = current.Count - 1; i >= 0; i--)
                {
                    if (!wanted.Contains(current[i]))
                    {
                        _box.Items.RemoveAt(i);
                    }
                }

                for (var i = 0; i < wanted.Count; i++)
                {
                    if (i >= _box.Items.Count || (string)_box.Items[i] != wanted[i])
                    {
                        _box.Items.Insert(i, wanted[i]);
                    }
                }
            }

            var at = Filter is null ? -1 : sessions.ToList().FindIndex(s => string.Equals(s, Filter, StringComparison.OrdinalIgnoreCase));
            if (Filter is not null && at < 0)
            {
                Filter = null;
            }

            var label = Filter is null ? AllSessions : wanted[1 + at];
            if (!Equals(_box.SelectedItem, label))
            {
                _box.SelectedItem = label;
            }
        }
        finally
        {
            _updating = false;
        }
    }
}
