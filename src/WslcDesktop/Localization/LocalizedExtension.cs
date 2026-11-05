using Microsoft.UI.Xaml.Markup;

namespace WslcDesktop.Localization;

/// <summary>Connects XAML labels to the shared UI resource catalog.</summary>
[MarkupExtensionReturnType(ReturnType = typeof(string))]
public sealed class LocalizedExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    protected override object ProvideValue() => UiStrings.Get(Key);
}
