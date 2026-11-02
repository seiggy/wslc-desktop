using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WslcDesktop.Views;

public sealed partial class SessionsPage : Page
{
    public SessionsPage()
    {
        InitializeComponent();
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
            WslText.Text = env.Wsl.Length > 0 ? env.Wsl : "–";
            KernelText.Text = env.Kernel.Length > 0 ? env.Kernel : "–";
            ManagerText.Text = env.SessionManager.Length > 0 ? env.SessionManager : "–";
            List.ItemsSource = env.Sessions;
        }
        catch (Exception e) when (showErrors)
        {
            await new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Couldn't load the environment",
                Content = e.Message,
                CloseButtonText = "Close",
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
        if (((FrameworkElement)sender).Tag is not Services.SessionInfo s)
        {
            return;
        }

        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Terminate session \"{s.Name}\"?",
            Content = "All containers running in this session will be stopped. This can't be undone.",
            PrimaryButtonText = "Terminate",
            CloseButtonText = "Cancel",
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
            await new ContentDialog { XamlRoot = XamlRoot, Title = "Couldn't terminate the session", Content = ex.Message, CloseButtonText = "Close" }.ShowAsync();
        }

        await RefreshAsync(true);
    }
}
