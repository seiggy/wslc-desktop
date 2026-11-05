using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WslcDesktop.Controls;
using WslcDesktop.Updates;

namespace WslcDesktop.Views.SettingsTabs;

/// <summary>The installed version and checking for, downloading and applying updates.</summary>
public sealed class UpdatesTab : SettingsTab
{
    private readonly Action _render;
    private readonly Action _onChanged;

    public UpdatesTab(StatusReporter report) : base(report)
    {
        var updates = App.Updates;
        var card = InspectUi.Card("Updates", out var rows);
        rows.Spacing = 12;

        var version = InspectUi.Description($"Installed version: {updates.CurrentVersion ?? "unknown"}");
        var title = new TextBlock { Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] };
        var message = InspectUi.Description(string.Empty);
        var progress = new ProgressBar { Minimum = 0, Maximum = 100 };
        var check = new Button { Content = "Check for updates" };
        var download = new Button { Content = "Download update", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        var restart = new Button { Content = "Restart to update", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        check.Click += async (_, _) => await updates.CheckAsync();
        download.Click += async (_, _) => await updates.DownloadAsync();
        restart.Click += (_, _) => updates.ApplyAndRestart();

        _render = () =>
        {
            var view = updates.View;
            title.Text = view.Title;
            message.Text = view.Message;
            progress.Visibility = view.ShowProgress ? Visibility.Visible : Visibility.Collapsed;
            progress.IsIndeterminate = updates.State == UpdateState.Checking;
            progress.Value = updates.Progress;
            check.IsEnabled = view.CanCheck;
            check.Visibility = updates.IsAvailable ? Visibility.Visible : Visibility.Collapsed;
            download.Visibility = view.CanDownload ? Visibility.Visible : Visibility.Collapsed;
            restart.Visibility = view.CanRestart ? Visibility.Visible : Visibility.Collapsed;
        };
        _onChanged = () => DispatcherQueue.TryEnqueue(() => _render());

        // Subscribe only while shown: the tab is detached when another tab is selected.
        Loaded += (_, _) =>
        {
            updates.Changed += _onChanged;
            _render();
        };
        Unloaded += (_, _) => updates.Changed -= _onChanged;

        rows.Children.Add(version);
        rows.Children.Add(title);
        rows.Children.Add(message);
        rows.Children.Add(progress);
        rows.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { check, download, restart } });
        Children.Add(card);
    }
}
