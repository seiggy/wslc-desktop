using WslcDesktop.Localization;
namespace WslcDesktop.Settings;

/// <summary>How the saved theme and accent colour are presented in the Appearance settings.</summary>
public static class AppearanceChoices
{
    private static readonly string[] Themes = ["System", "Light", "Dark"];

    public static IReadOnlyList<string> ThemeLabels => [UiStrings.Get("UseWindowsSetting"), UiStrings.Get("Light"), UiStrings.Get("Dark")];

    public static int ThemeIndex(string theme)
    {
        var i = Array.IndexOf(Themes, theme);
        return i < 0 ? 0 : i;
    }

    public static string ThemeFromIndex(int index) => index >= 0 && index < Themes.Length ? Themes[index] : Themes[0];
}
