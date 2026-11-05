using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WslcDesktop.Controls;
using WslcDesktop.Services;
using WslcDesktop.Resources;

using WslcDesktop.Sessions;

namespace WslcDesktop.Views;

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
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(BackButton, _kind == ResourceKind.Volume ? "Back to volumes" : "Back to networks");
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
            var card = InspectUi.Card("Overview", out var rows);
            InspectUi.AddRow(rows, "Name", Str(root, "Name"));
            InspectUi.AddRow(rows, "Session", App.Host.IsAppSession(_session) ? $"{_session} (this app)" : _session);
            InspectUi.AddRow(rows, "ID", Str(root, "Id"), true);
            InspectUi.AddRow(rows, "Driver", Str(root, "Driver"));
            InspectUi.AddRow(rows, "Scope", Str(root, "Scope"));
            InspectUi.AddRow(rows, "Created", InspectUi.FormatDate(Str(root, "CreatedAt").Length > 0 ? Str(root, "CreatedAt") : Str(root, "Created")));
            if (_kind == ResourceKind.Volume)
            {
                InspectUi.AddRow(rows, "Mountpoint", Str(root, "Mountpoint"), true);
            }
            else
            {
                InspectUi.AddRow(rows, "Access", Bool(root, "Internal") ? "Internal (no external access)" : "External");
                InspectUi.AddRow(rows, "IPv4", Bool(root, "EnableIPv4") ? "Enabled" : "Disabled");
                InspectUi.AddRow(rows, "IPv6", Bool(root, "EnableIPv6") ? "Enabled" : "Disabled");
                if (root.TryGetProperty("IPAM", out var ipam) && ipam.TryGetProperty("Config", out var cfg) && cfg.ValueKind == JsonValueKind.Array)
                {
                    InspectUi.AddRow(rows, "Subnets", string.Join('\n', cfg.EnumerateArray().Select(c =>
                        Str(c, "Subnet") + (Str(c, "Gateway").Length > 0 ? $" · gateway {Str(c, "Gateway")}" : string.Empty))), true);
                }
            }

            Cards.Children.Add(card);

            card = InspectUi.Card("Used by", out rows);
            var users = await UsersAsync(root);
            InspectUi.AddRow(rows, "Containers", users.Count > 0 ? string.Join('\n', users) : "No containers use this " + (_kind == ResourceKind.Volume ? "volume" : "network"));
            Cards.Children.Add(card);

            card = InspectUi.Card("Metadata", out rows);
            InspectUi.AddRow(rows, "Labels", Pairs(root, "Labels"), true);
            InspectUi.AddRow(rows, "Options", Pairs(root, "Options"), true);
            if (rows.Children.Count == 0)
            {
                InspectUi.AddRow(rows, "Labels", "None");
            }

            Cards.Children.Add(card);
            RawView.SetJson(json);
            ErrorBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            ErrorBar.Title = $"Couldn't inspect {_name}";
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
