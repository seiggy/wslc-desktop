using System.Globalization;
using System.Resources;

namespace WslcDesktop.Localization;

/// <summary>Loads UI text using the current display language, with English as the fallback.</summary>
public static class UiStrings
{
    private static readonly ResourceManager Resources = new("WslcDesktop.Localization.UiStrings", typeof(UiStrings).Assembly);

    /// <summary>Returns a localized string. Missing keys are programming errors rather than empty labels.</summary>
    public static string Get(string key) =>
        Resources.GetString(key, CultureInfo.CurrentUICulture)
        ?? throw new MissingManifestResourceException($"UI resource '{key}' was not found.");

    /// <summary>Formats localized text with values using the current formatting culture.</summary>
    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
