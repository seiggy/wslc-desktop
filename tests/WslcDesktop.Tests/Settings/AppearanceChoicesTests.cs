using WslcDesktop.Settings;

namespace WslcDesktop.Tests.Settings;

public class AppearanceChoicesTests
{
    [Theory]
    [InlineData("System", 0)]
    [InlineData("Light", 1)]
    [InlineData("Dark", 2)]
    [InlineData("anything else", 0)]
    public void Theme_names_map_to_picker_positions(string theme, int index) =>
        Assert.Equal(index, AppearanceChoices.ThemeIndex(theme));

    [Theory]
    [InlineData(0, "System")]
    [InlineData(1, "Light")]
    [InlineData(2, "Dark")]
    [InlineData(-1, "System")]
    [InlineData(9, "System")]
    public void Picker_positions_map_back_to_theme_names(int index, string theme) =>
        Assert.Equal(theme, AppearanceChoices.ThemeFromIndex(index));

    [Fact]
    public void The_labels_line_up_with_the_positions() =>
        Assert.Equal(["Use Windows setting", "Light", "Dark"], AppearanceChoices.ThemeLabels);

    [Theory]
    [InlineData(0x00, 0x78, 0xD4, "#0078D4")]
    [InlineData(0xFF, 0x0A, 0x00, "#FF0A00")]
    public void A_color_formats_as_an_uppercase_hex_string(byte r, byte g, byte b, string hex) =>
        Assert.Equal(hex, AccentPalette.ToHex(r, g, b));

    [Fact]
    public void A_hex_string_splits_back_into_channels() =>
        Assert.Equal((0x00, 0x78, 0xD4), AccentPalette.Channels("#0078D4"));

    [Fact]
    public void Channels_round_trip_through_hex()
    {
        var (r, g, b) = AccentPalette.Channels(AccentPalette.ToHex(12, 200, 99));

        Assert.Equal((12, 200, 99), (r, g, b));
    }
}
