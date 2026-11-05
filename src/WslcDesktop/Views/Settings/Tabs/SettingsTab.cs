using Microsoft.UI.Xaml.Controls;

namespace WslcDesktop.Views.Settings.Tabs;

/// <summary>One page of the settings: a vertical stack of cards for a single concern.</summary>
public abstract class SettingsTab : StackPanel
{
    protected SettingsTab(StatusReporter report)
    {
        Spacing = 16;
        Report = report;
    }

    protected StatusReporter Report { get; }
}
