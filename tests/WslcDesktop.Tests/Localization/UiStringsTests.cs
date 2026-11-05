using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text;
using System.Text.RegularExpressions;
using WslcDesktop.Localization;

namespace WslcDesktop.Tests.Localization;

public class UiStringsTests
{
    [Theory]
    [InlineData("en", "Retry")]
    [InlineData("es", "Reintentar")]
    [InlineData("fr", "Réessayer")]
    [InlineData("de", "Erneut versuchen")]
    [InlineData("it", "Riprova")]
    [InlineData("ja", "再試行")]
    [InlineData("zh-Hans", "重试")]
    [InlineData("es-MX", "Reintentar")]
    [InlineData("zh-CN", "重试")]
    [InlineData("nl-NL", "Retry")]
    public void ResolvesDisplayLanguageAndFallsBackToEnglish(string language, string expected)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            Assert.Equal(expected, UiStrings.Get("ActionRetry"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void MissingKeysFailExplicitly() =>
        Assert.Throws<MissingManifestResourceException>(() => UiStrings.Get("DoesNotExist"));

    [Theory]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("it")]
    [InlineData("ja")]
    [InlineData("zh-Hans")]
    public void TranslationCatalogsHaveEveryKeyAndPreserveFormatArguments(string language)
    {
        var resources = new ResourceManager("WslcDesktop.Localization.UiStrings", typeof(UiStrings).Assembly);
        var english = resources.GetResourceSet(CultureInfo.GetCultureInfo("en"), true, false);
        var translated = resources.GetResourceSet(CultureInfo.GetCultureInfo(language), true, false);
        Assert.NotNull(english);
        Assert.NotNull(translated);
        var source = english.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
        var target = translated.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
        Assert.Equal(source.Keys.Order(), target.Keys.Order());
        foreach (var (key, text) in source)
        {
            Assert.False(string.IsNullOrWhiteSpace(target[key]), key);
            Assert.Equal(Arguments(text), Arguments(target[key]));
            if (IsCompositeFormat(text))
            {
                Assert.True(IsCompositeFormat(target[key]), $"Invalid composite format in {language}: {key}");
            }
        }
    }

    private static bool IsCompositeFormat(string text)
    {
        try
        {
            CompositeFormat.Parse(text);
            return true;
        }
        catch (FormatException)
        {
            // Some plain labels describe literal template syntax rather than format arguments.
            return false;
        }
    }

    private static string[] Arguments(string text) =>
        Regex.Matches(text, @"(?<!\{)\{(\d+)(?:[^{}]*)\}(?!\})")
            .Select(m => m.Groups[1].Value).Distinct().Order().ToArray();
}
