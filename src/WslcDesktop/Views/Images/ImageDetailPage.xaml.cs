using WslcDesktop.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WslcDesktop.Services;
using WslcDesktop.Containers.Creation;
using WslcDesktop.Images;
using static WslcDesktop.Controls.InspectUi;

namespace WslcDesktop.Views.Images;

public sealed partial class ImageDetailPage : Page
{
    private string _target = string.Empty;
    private string _session = string.Empty;

    public ImageDetailPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var image = (ImageRef)e.Parameter;
        _target = image.Target;
        _session = image.Session;
        TitleText.Text = _target.StartsWith("sha256:", StringComparison.Ordinal) ? UiStrings.Get("UntaggedImage") : _target;
    }

    private string _runReference = string.Empty;
    private bool _canCopy;

    private async void Run_Click(object sender, RoutedEventArgs e) =>
        await ImagesPage.RunFromAsync(XamlRoot, Frame, _session, _runReference, _canCopy);

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
            detail = await App.Host.InspectImageAsync(_session, _target);
        }
        catch (Exception ex)
        {
            ErrorBar.Title = UiStrings.Get("CouldnTInspectTheImage");
            ErrorBar.Message = ex.Message;
            ErrorBar.IsOpen = true;
            return;
        }

        var tagged = detail.Tags.Count > 0;
        TitleText.Text = tagged ? detail.Tags[0] : UiStrings.Get("UntaggedImage");
        _runReference = RunImage.Reference(detail.Tags, detail.Id);
        _canCopy = tagged;
        RunButton.IsEnabled = true;

        AddRow(OverviewRows, UiStrings.Get("LabelSession"), App.Host.IsAppSession(_session) ? UiStrings.Format("ThisApp2", _session) : _session);
        AddRow(OverviewRows, UiStrings.Get("LabelStatus"), tagged ? UiStrings.Get("Tagged") : UiStrings.Get("Untagged"));
        AddRow(OverviewRows, UiStrings.Get("LabelTags"), tagged ? string.Join('\n', detail.Tags) : UiStrings.Get("None"));
        AddRow(OverviewRows, UiStrings.Get("LabelImageId"), detail.Id);
        AddRow(OverviewRows, UiStrings.Get("LabelDigest"), detail.Digests.Count > 0 ? string.Join('\n', detail.Digests) : UiStrings.Get("NoneNotPulledFromARegistry"));
        AddRow(OverviewRows, UiStrings.Get("LabelSize"), detail.Size);
        AddRow(OverviewRows, UiStrings.Get("LabelCreated"), FormatCreated(detail.Created));
        AddRow(OverviewRows, UiStrings.Get("LabelOsArch"), $"{detail.Os}/{detail.Architecture}");
        AddRow(OverviewRows, UiStrings.Get("LabelAuthor"), detail.Author);
        AddRow(OverviewRows, UiStrings.Get("LabelComment"), detail.Comment);

        LayersTitle.Text = UiStrings.Format("Layers2", detail.Layers.Count);
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

        AddRow(ConfigRows, UiStrings.Get("LabelEntrypoint"), Join(detail.Entrypoint));
        AddRow(ConfigRows, UiStrings.Get("LabelCommand"), Join(detail.Cmd));
        AddRow(ConfigRows, UiStrings.Get("LabelWorkingDir"), detail.WorkingDir);
        AddRow(ConfigRows, UiStrings.Get("LabelUser"), detail.User);
        AddRow(ConfigRows, UiStrings.Get("LabelExposedPorts"), string.Join(", ", detail.Ports));
        AddRow(ConfigRows, UiStrings.Get("LabelEnvironment"), string.Join('\n', detail.Env));
        AddRow(ConfigRows, UiStrings.Get("LabelLabels"), string.Join('\n', detail.Labels));

        RawView.SetJson(detail.RawJson);
    }

    private static string Join(IReadOnlyList<string> parts) => string.Join(' ', parts);

    private static string FormatCreated(string created) =>
        DateTimeOffset.TryParse(created, out var d) ? d.ToLocalTime().ToString("f") : created;
}
