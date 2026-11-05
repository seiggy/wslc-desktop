using WslcDesktop.Compose.Planning.Resources;

using WslcDesktop.Compose.Execution;
using WslcDesktop.Compose.Parsing;
using WslcDesktop.Compose.Planning;

namespace WslcDesktop.Compose.Tests.Planning;

public class HelperTests
{
    [Theory]
    [InlineData("a $$b", "a $b")]
    [InlineData("${X}", "x")]
    [InlineData("$X-y", "x-y")]
    [InlineData("${MISSING:-def}", "def")]
    [InlineData("${EMPTY:-def}", "def")]
    [InlineData("${EMPTY-def}", "")]
    [InlineData("${X:+alt}", "alt")]
    [InlineData("${MISSING:+alt}", "")]
    [InlineData("${MISSING:-${X}}", "x")]
    public void Interpolates(string input, string expected)
    {
        var env = new Dictionary<string, string> { ["X"] = "x", ["EMPTY"] = string.Empty };

        Assert.Equal(expected, Interpolator.Apply(input, env));
    }

    [Fact]
    public void Required_variable_throws()
    {
        var ex = Assert.Throws<ComposeException>(() => Interpolator.Apply("${DB:?need a db}", new Dictionary<string, string>()));

        Assert.Contains("need a db", ex.Message);
    }

    [Fact]
    public void Parses_env_files()
    {
        var env = Interpolator.ParseEnvFile("# c\nA=1\nexport B=\"two words\"\nC='x' \nD=v # tail\n\nbad\n");

        Assert.Equal("1", env["A"]);
        Assert.Equal("two words", env["B"]);
        Assert.Equal("x", env["C"]);
        Assert.Equal("v", env["D"]);
        Assert.Equal(4, env.Count);
    }

    [Theory]
    [InlineData("data:/d", "data", "/d", false)]
    [InlineData("data:/d:ro", "data", "/d", true)]
    [InlineData(@"C:\src\app:/app", @"C:\src\app", "/app", false)]
    [InlineData(@"C:/src:/app:ro", "C:/src", "/app", true)]
    [InlineData("/anon", "/anon", null, false)]
    public void Splits_volume_specs(string text, string source, string? target, bool ro) =>
        Assert.Equal((source, target, ro), VolumeSpec.SplitVolume(text));

    [Theory]
    [InlineData("echo hi", new[] { "echo", "hi" })]
    [InlineData("sh -c \"echo a b\"", new[] { "sh", "-c", "echo a b" })]
    [InlineData("a 'b c' d", new[] { "a", "b c", "d" })]
    public void Shell_splits(string text, string[] expected) =>
        Assert.Equal(expected, ComposeValues.ShellSplit(text));

    [Theory]
    [InlineData("10s", 10)]
    [InlineData("1m30s", 90)]
    [InlineData("500ms", 1)]
    [InlineData("1h", 3600)]
    [InlineData("bogus", null)]
    public void Parses_durations(string text, int? seconds) =>
        Assert.Equal(seconds, ComposeUnits.ParseDurationSeconds(text));

    [Fact]
    public void Parses_stack_labels_out_of_wslc_label_string()
    {
        const string labels = "com.microsoft.wsl.container.metadata={\"V1\":{\"Flags\":0,\"Ports\":[]}},com.wslc.compose.project=demo,com.wslc.compose.service=web";

        Assert.True(ComposeLabels.TryParse(labels, out var project, out var service));
        Assert.Equal("demo", project);
        Assert.Equal("web", service);
        Assert.False(ComposeLabels.TryParse("a=b", out _, out _));
    }
}
