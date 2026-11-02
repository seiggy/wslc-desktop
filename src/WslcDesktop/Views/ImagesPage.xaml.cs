using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.WSL.Containers;

namespace WslcDesktop.Views;

public sealed partial class ImagesPage : Page
{
    public ImagesPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            var images = await App.Host.GetImagesAsync();
            List.ItemsSource = images;
            EmptyText.Visibility = images.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception e)
        {
            await ShowErrorAsync("Couldn't list images", e);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void PullBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            await PullAsync();
        }
    }

    private async void Pull_Click(object sender, RoutedEventArgs e) => await PullAsync();

    private async Task PullAsync()
    {
        var uri = PullBox.Text.Trim();
        if (uri.Length == 0)
        {
            return;
        }

        PullButton.IsEnabled = false;
        PullStatus.Visibility = Visibility.Visible;
        PullText.Text = $"Pulling {uri}…";
        PullBar.IsIndeterminate = true;
        try
        {
            var progress = new Progress<ImageProgress>(p =>
            {
                PullText.Text = $"{p.Status} {p.Id}";
                if (p.TotalBytes > 0)
                {
                    PullBar.IsIndeterminate = false;
                    PullBar.Maximum = p.TotalBytes;
                    PullBar.Value = p.CurrentBytes;
                }
            });
            await App.Host.PullImageAsync(uri, progress);
            PullBox.Text = string.Empty;
        }
        catch (Exception e)
        {
            await ShowErrorAsync($"Couldn't pull {uri}", e);
        }
        finally
        {
            PullStatus.Visibility = Visibility.Collapsed;
            PullButton.IsEnabled = true;
        }

        await RefreshAsync();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not string name)
        {
            return;
        }

        try
        {
            await App.Host.DeleteImageAsync(name);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync($"Couldn't delete {name}", ex);
        }

        await RefreshAsync();
    }

    private async Task ShowErrorAsync(string title, Exception e)
    {
        await new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = e.Message,
            CloseButtonText = "Close",
        }.ShowAsync();
    }
}
