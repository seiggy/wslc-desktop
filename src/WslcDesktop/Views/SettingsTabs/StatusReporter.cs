using Microsoft.UI.Xaml.Controls;

namespace WslcDesktop.Views.SettingsTabs;

/// <summary>Shows a message in the settings page's status bar.</summary>
public delegate void StatusReporter(string title, string message, InfoBarSeverity severity);
