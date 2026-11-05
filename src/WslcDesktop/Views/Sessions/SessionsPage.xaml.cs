using WslcDesktop.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WslcDesktop.ViewModels;

namespace WslcDesktop.Views.Sessions;

public sealed partial class SessionsPage : Page
{
    private readonly System.Collections.ObjectModel.ObservableCollection<SessionItem> _items = [];

    private static void SetText(TextBlock box, string value)
    {
        var text = value.Length > 0 ? value : "–";
        if (box.Text != text)
        {
            box.Text = text;
        }
    }

    public SessionsPage()
    {
        InitializeComponent();
        List.ItemsSource = _items;
        var timer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += async (_, _) => await RefreshAsync(false);
        Loaded += async (_, _) =>
        {
            await RefreshAsync(true);
            timer.Start();
        };
        Unloaded += (_, _) => timer.Stop();
    }

    private bool _busy;

    private async Task RefreshAsync(bool showErrors)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            var env = await App.Host.GetEnvironmentAsync();
            SetText(WslText, env.Wsl);
            SetText(KernelText, env.Kernel);
            SetText(ManagerText, env.SessionManager);

            foreach (var gone in _items.Where(i => env.Sessions.All(s => s.Id != i.Id)).ToList())
            {
                _items.Remove(gone);
            }

            for (var n = 0; n < env.Sessions.Count; n++)
            {
                var item = _items.FirstOrDefault(i => i.Id == env.Sessions[n].Id);
                if (item is null)
                {
                    _items.Insert(Math.Min(n, _items.Count), new SessionItem(env.Sessions[n]));
                }
                else
                {
                    item.Update(env.Sessions[n]);
                }
            }
        }
        catch (Exception e) when (showErrors)
        {
            await new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = UiStrings.Get("CouldnTLoadTheEnvironment"),
                Content = e.Message,
                CloseButtonText = UiStrings.Get("ActionClose"),
            }.ShowAsync();
        }
        catch
        {
        }
        finally
        {
            _busy = false;
        }
    }

    private async void Terminate_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not string sessionName)
        {
            return;
        }

        var s = new { Name = sessionName };

        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = UiStrings.Format("TerminateSession2", s.Name),
            Content = UiStrings.Get("AllContainersRunningInThisSessionWillBeStopped"),
            PrimaryButtonText = UiStrings.Get("Terminate"),
            CloseButtonText = UiStrings.Get("ActionCancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            await App.Host.TerminateSessionAsync(s.Name);
        }
        catch (Exception ex)
        {
            await new ContentDialog { XamlRoot = XamlRoot, Title = UiStrings.Get("CouldnTTerminateTheSession"), Content = ex.Message, CloseButtonText = UiStrings.Get("ActionClose") }.ShowAsync();
        }

        await RefreshAsync(true);
    }
}
