using WslcDesktop.Localization;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WslcDesktop.Controls;
using WslcDesktop.Services;
using WslcDesktop.Resources;

using WslcDesktop.Sessions;

namespace WslcDesktop.Views.Resources;

/// <summary>Readable inspect view for a volume or network, with the raw JSON underneath.</summary>
public sealed partial class ResourceDetailPage : Page
{
    private ResourceKind _kind;
    private string _name = string.Empty;
    private string _session = string.Empty;

    public ResourceDetailPage() => InitializeComponent();

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    private static ResourceRef LegacyRef((ResourceKind Kind, string Name) p) => new(p.Kind, App.Host.SessionId, p.Name);

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        var target = e.Parameter is ResourceRef r ? r : LegacyRef(((ResourceKind, string))e.Parameter);
        (_kind, _session, _name) = (target.Kind, target.Session, target.Name);
        TitleText.Text = _name;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(BackButton, _kind == ResourceKind.Volume ? UiStrings.Get("BackToVolumes") : UiStrings.Get("BackToNetworks"));
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var json = await App.Host.Operations.Resources.InspectAsync(App.Host.Handle(_session), _kind, _name);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0
                ? doc.RootElement[0]
                : doc.RootElement;

            Cards.Children.Clear();
            var card = InspectUi.Card(UiStrings.Get("Overview"), out var rows);
            InspectUi.AddRow(rows, UiStrings.Get("LabelName"), Str(root, "Name"));
            InspectUi.AddRow(rows, UiStrings.Get("LabelSession"), App.Host.IsAppSession(_session) ? UiStrings.Format("ThisApp2", _session) : _session);
            InspectUi.AddRow(rows, UiStrings.Get("LabelId"), Str(root, "Id"), true);
            InspectUi.AddRow(rows, UiStrings.Get("LabelDriver"), Str(root, "Driver"));
            InspectUi.AddRow(rows, UiStrings.Get("LabelScope"), Str(root, "Scope"));
            InspectUi.AddRow(rows, UiStrings.Get("LabelCreated"), InspectUi.FormatDate(Str(root, "CreatedAt").Length > 0 ? Str(root, "CreatedAt") : Str(root, "Created")));
            if (_kind == ResourceKind.Volume)
            {
                InspectUi.AddRow(rows, UiStrings.Get("LabelMountpoint"), Str(root, "Mountpoint"), true);
            }
            else
            {
                InspectUi.AddRow(rows, UiStrings.Get("LabelAccess"), Bool(root, "Internal") ? UiStrings.Get("InternalNoExternalAccess") : UiStrings.Get("External"));
                InspectUi.AddRow(rows, UiStrings.Get("LabelIpv4"), Bool(root, "EnableIPv4") ? UiStrings.Get("Enabled") : UiStrings.Get("Disabled"));
                InspectUi.AddRow(rows, UiStrings.Get("LabelIpv6"), Bool(root, "EnableIPv6") ? UiStrings.Get("Enabled") : UiStrings.Get("Disabled"));
                if (root.TryGetProperty("IPAM", out var ipam) && ipam.TryGetProperty("Config", out var cfg) && cfg.ValueKind == JsonValueKind.Array)
                {
                    InspectUi.AddRow(rows, UiStrings.Get("LabelSubnets"), string.Join('\n', cfg.EnumerateArray().Select(c =>
                        Str(c, "Subnet") + (Str(c, "Gateway").Length > 0 ? UiStrings.Format("Gateway2", Str(c, "Gateway")) : string.Empty))), true);
                }
            }

            Cards.Children.Add(card);

            card = InspectUi.Card(UiStrings.Get("LabelUsedBy"), out rows);
            var users = await UsersAsync(root);
            InspectUi.AddRow(rows, UiStrings.Get("LabelContainers"), users.Count > 0 ? string.Join('\n', users)
                : UiStrings.Get(_kind == ResourceKind.Volume ? "NoContainersUseVolume" : "NoContainersUseNetwork"));
            Cards.Children.Add(card);

            card = InspectUi.Card(UiStrings.Get("LabelMetadata"), out rows);
            InspectUi.AddRow(rows, UiStrings.Get("LabelLabels"), Pairs(root, "Labels"), true);
            InspectUi.AddRow(rows, UiStrings.Get("LabelOptions"), Pairs(root, "Options"), true);
            if (rows.Children.Count == 0)
            {
                InspectUi.AddRow(rows, UiStrings.Get("LabelLabels"), UiStrings.Get("None"));
            }

            Cards.Children.Add(card);
            RawView.SetJson(json);
            ErrorBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            ErrorBar.Title = UiStrings.Format("CouldnTInspect2", _name);
            ErrorBar.Message = ex.Message;
            ErrorBar.IsOpen = true;
        }
    }

    private async Task<List<string>> UsersAsync(JsonElement root)
    {
        if (_kind == ResourceKind.Network)
        {
            return root.TryGetProperty("Containers", out var c) && c.ValueKind == JsonValueKind.Object
                ? c.EnumerateObject().Select(o => $"{Str(o.Value, "Name")}  {Str(o.Value, "IPv4Address")}".TrimEnd()).ToList()
                : [];
        }

        try
        {
            var rows = await App.Host.Operations.Resources.ListAsync(new SessionHandle(_session, App.Host.IsAppSession(_session)), ResourceKind.Volume);
            return rows.FirstOrDefault(r => r.Name == _name)?.UsedBy.ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string Pairs(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object
            ? string.Join('\n', v.EnumerateObject().Select(p => $"{p.Name}={p.Value}"))
            : string.Empty;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty
            : string.Empty;
}
