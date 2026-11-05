using WslcDesktop.Localization;
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
        var card = InspectUi.Card(UiStrings.Get("AppSessionWslcDesktop"), out var rows);
        rows.Spacing = 12;
        rows.Children.Add(InspectUi.Description(
            UiStrings.Get("ResourcesForTheSessionWSLcDesktopManagerRunsYour")));
        InspectUi.AddRow(rows, UiStrings.Get("RunningWith"), UiStrings.Format("CPUsMBMemory", host.StartedCpuCount, host.StartedMemoryMb));
        InspectUi.AddRow(rows, UiStrings.Get("Storage"), Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WslcDesktop", "Data"), true);

        var settings = host.Settings;
        var cpu = new NumberBox
        {
            Header = UiStrings.Format("CPUCount2", Environment.ProcessorCount),
            Minimum = 1,
            Maximum = Environment.ProcessorCount,
            Value = settings.CpuCount,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        var memory = new NumberBox
        {
            Header = UiStrings.Get("MemoryMB"),
            Minimum = 512,
            Value = settings.MemoryMb,
            SmallChange = 512,
            LargeChange = 1024,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        var gpu = new ToggleSwitch { Header = UiStrings.Get("GPUAccess"), IsOn = settings.EnableGpu };

        var save = new Button { Content = UiStrings.Get("ActionSave"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        save.Click += (_, _) =>
        {
            if (!settings.TryApplySessionResources(cpu.Value, memory.Value, gpu.IsOn, out var problem))
            {
                report(UiStrings.Get("CheckTheValues"), problem!, InfoBarSeverity.Warning);
                return;
            }

            try
            {
                settings.Save();
                report(UiStrings.Get("Saved"), UiStrings.Get("RestartWSLcDesktopManagerToApplyTheNewSession"), InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                report(UiStrings.Get("CouldnTSaveAppSettings"), ex.Message, InfoBarSeverity.Error);
            }
        };
        var reset = new Button { Content = UiStrings.Get("ResetToDefaults") };
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
