using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WslcDesktop.Controls;
using WslcDesktop.Settings;

namespace WslcDesktop.Views.Settings.Tabs.Cards;

/// <summary>CPU, memory and GPU for the session the app runs your containers in. Applied when the app next starts.</summary>
public static class AppSessionCard
{
    public static Border Create(WslcDesktop.Services.WslcHost host, StatusReporter report)
    {
        var card = InspectUi.Card("App session (WslcDesktop)", out var rows);
        rows.Spacing = 12;
        rows.Children.Add(InspectUi.Description(
            "Resources for the session WSLc Desktop Manager runs your containers in. These are passed to WSLc when the app starts, "
            + "so changes apply the next time you open the app."));
        InspectUi.AddRow(rows, "Running with", $"{host.StartedCpuCount} CPUs, {host.StartedMemoryMb:N0} MB memory");
        InspectUi.AddRow(rows, "Storage", Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WslcDesktop", "Data"), true);

        var settings = host.Settings;
        var cpu = new NumberBox
        {
            Header = $"CPU count (1–{Environment.ProcessorCount})",
            Minimum = 1,
            Maximum = Environment.ProcessorCount,
            Value = settings.CpuCount,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        var memory = new NumberBox
        {
            Header = "Memory (MB)",
            Minimum = 512,
            Value = settings.MemoryMb,
            SmallChange = 512,
            LargeChange = 1024,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        var gpu = new ToggleSwitch { Header = "GPU access", IsOn = settings.EnableGpu };

        var save = new Button { Content = "Save", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        save.Click += (_, _) =>
        {
            if (!settings.TryApplySessionResources(cpu.Value, memory.Value, gpu.IsOn, out var problem))
            {
                report("Check the values", problem!, InfoBarSeverity.Warning);
                return;
            }

            try
            {
                settings.Save();
                report("Saved", "Restart WSLc Desktop Manager to apply the new session resources.", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                report("Couldn't save app settings", ex.Message, InfoBarSeverity.Error);
            }
        };
        var reset = new Button { Content = "Reset to defaults" };
        reset.Click += (_, _) =>
        {
            cpu.Value = AppSettings.DefaultCpuCount;
            memory.Value = AppSettings.DefaultMemoryMb;
            gpu.IsOn = false;
        };

        rows.Children.Add(cpu);
        rows.Children.Add(memory);
        rows.Children.Add(gpu);
        rows.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, reset } });
        return card;
    }
}
