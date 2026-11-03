using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WslcDesktop.Services;
using static WslcDesktop.Controls.InspectUi;

namespace WslcDesktop.Views;

public sealed partial class ImageDetailPage : Page
{
    private string _target = string.Empty;

    public ImageDetailPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _target = (string)e.Parameter;
        TitleText.Text = _target.StartsWith("sha256:", StringComparison.Ordinal) ? "Untagged image" : _target;
    }

    private string _runReference = string.Empty;

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        var created = await ContainersPage.ShowNewContainerDialogAsync(XamlRoot, _runReference);
        if (created is not null)
        {
            await ImagesPage.OfferOpenContainerAsync(XamlRoot, Frame, created);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
        else
        {
            Frame.Navigate(typeof(ImagesPage));
        }
    }

    private async Task LoadAsync()
    {
        ImageDetail detail;
        try
        {
            detail = await App.Host.InspectImageAsync(_target);
        }
        catch (Exception ex)
        {
            ErrorBar.Title = "Couldn't inspect the image";
            ErrorBar.Message = ex.Message;
            ErrorBar.IsOpen = true;
            return;
        }

        var tagged = detail.Tags.Count > 0;
        TitleText.Text = tagged ? detail.Tags[0] : "Untagged image";
        _runReference = RunImage.Reference(detail.Tags, detail.Id);
        RunButton.IsEnabled = true;

        AddRow(OverviewRows, "Status", tagged ? "Tagged" : "Untagged");
        AddRow(OverviewRows, "Tags", tagged ? string.Join('\n', detail.Tags) : "None");
        AddRow(OverviewRows, "Image ID", detail.Id);
        AddRow(OverviewRows, "Digest", detail.Digests.Count > 0 ? string.Join('\n', detail.Digests) : "None (not pulled from a registry)");
        AddRow(OverviewRows, "Size", detail.Size);
        AddRow(OverviewRows, "Created", FormatCreated(detail.Created));
        AddRow(OverviewRows, "OS / Arch", $"{detail.Os}/{detail.Architecture}");
        AddRow(OverviewRows, "Author", detail.Author);
        AddRow(OverviewRows, "Comment", detail.Comment);

        LayersTitle.Text = $"Layers ({detail.Layers.Count})";
        for (var i = 0; i < detail.Layers.Count; i++)
        {
            var row = new Grid { ColumnSpacing = 16 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(Secondary((i + 1).ToString()));
            var digest = Mono(detail.Layers[i]);
            Grid.SetColumn(digest, 1);
            row.Children.Add(digest);
            LayerRows.Children.Add(row);
        }

        AddRow(ConfigRows, "Entrypoint", Join(detail.Entrypoint));
        AddRow(ConfigRows, "Command", Join(detail.Cmd));
        AddRow(ConfigRows, "Working dir", detail.WorkingDir);
        AddRow(ConfigRows, "User", detail.User);
        AddRow(ConfigRows, "Exposed ports", string.Join(", ", detail.Ports));
        AddRow(ConfigRows, "Environment", string.Join('\n', detail.Env));
        AddRow(ConfigRows, "Labels", string.Join('\n', detail.Labels));

        RawView.SetJson(detail.RawJson);
    }

    private static string Join(IReadOnlyList<string> parts) => string.Join(' ', parts);

    private static string FormatCreated(string created) =>
        DateTimeOffset.TryParse(created, out var d) ? d.ToLocalTime().ToString("f") : created;
}
