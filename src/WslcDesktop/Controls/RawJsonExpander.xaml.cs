using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace WslcDesktop.Controls;

/// <summary>Collapsed-by-default, scrollable, monospace JSON with a copy button.</summary>
public sealed partial class RawJsonExpander : UserControl
{
    private string _json = string.Empty;

    public RawJsonExpander()
    {
        InitializeComponent();
        Output.AutoScroll = false;
    }

    public void SetJson(string json)
    {
        _json = json;
        Output.Clear();
        foreach (var line in json.Split('\n'))
        {
            Output.Append(line.TrimEnd('\r'));
        }
    }

    private async void Copy_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(_json);
        Clipboard.SetContent(package);
        CopyButton.Content = "Copied";
        await Task.Delay(1500);
        CopyButton.Content = "Copy JSON";
    }
}
