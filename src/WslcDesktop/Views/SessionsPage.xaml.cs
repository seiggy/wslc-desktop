using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WslcDesktop.Views;

public sealed partial class SessionsPage : Page
{
    public SessionsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            List.ItemsSource = await App.Host.GetSessionsAsync();
        }
        catch (Exception e)
        {
            await new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Couldn't list sessions",
                Content = e.Message,
                CloseButtonText = "Close",
            }.ShowAsync();
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
}
