using WslcDesktop.Views.Containers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WslcDesktop.Services;
using WslcDesktop.ViewModels;

using WslcDesktop.Containers;
namespace WslcDesktop.Views.Stacks;

public sealed partial class StackDetailPage : Page
{
    private readonly System.Collections.ObjectModel.ObservableCollection<ContainerItem> _items = [];
    private readonly HashSet<string> _streaming = [];
    private CancellationTokenSource _cts = new();
    private string _project = string.Empty;
    private Shared.StoreBinding? _binding;

    public StackDetailPage()
    {
        InitializeComponent();
        Services.ItemsSource = _items;
        Loaded += (_, _) =>
        {
            _cts = new CancellationTokenSource();
            _binding = new Shared.StoreBinding(DispatcherQueue, Sync, [Data.DataKind.Containers]);
        };
        Unloaded += (_, _) =>
        {
            _binding?.Dispose();
            _binding = null;
            _cts.Cancel();
            _streaming.Clear();
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _project = (string)e.Parameter;
        TitleText.Text = _project;
    }

    private void Sync()
    {
        var rows = StackItem.Group(App.Host.Store.AppContainers.Latest ?? []).FirstOrDefault(g => g.Project == _project).Rows ?? [];
        foreach (var gone in _items.Where(i => rows.All(r => r.Name != i.Name)).ToList())
        {
            _items.Remove(gone);
        }

        foreach (var row in rows)
        {
            var item = _items.FirstOrDefault(i => i.Name == row.Name);
            if (item is null)
            {
                item = new ContainerItem(App.Host.SessionId, true, row.Name);
                _items.Add(item);
            }

            item.Update(row, []);
            if (ContainerItem.IsRunningState(row.State) && _streaming.Add(row.Name))
            {
                StreamLogs(row.Name, item.Service.Length > 0 ? item.Service : row.Name);
            }
            else if (!ContainerItem.IsRunningState(row.State))
            {
                _streaming.Remove(row.Name);
            }
        }
    }

    // One follow stream per running container, prefixed so the combined output stays attributable.
    private void StreamLogs(string container, string service)
    {
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await App.Host.Operations.Containers.StreamLogsAsync(App.Host.AppSession, container, new LogOptions(100, true, false), line => LogView.Append($"[{service}] {line}"), ct);
            }
            catch
            {
                // The stream ends when the container stops or the page closes.
            }

            DispatcherQueue.TryEnqueue(() => _streaming.Remove(container));
        });
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => LogView.Clear();

    private void Services_ItemClick(object sender, ItemClickEventArgs e) =>
        Frame.Navigate(typeof(ContainerDetailPage), ((ContainerItem)e.ClickedItem).Name);

    private void Back_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(StacksPage));
}
