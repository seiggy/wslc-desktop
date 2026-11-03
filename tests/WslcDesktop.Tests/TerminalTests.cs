using System.Text;
using WslcDesktop.Services;

namespace WslcDesktop.Tests;

public class TerminalTests
{
    [Theory]
    [InlineData("wslc", "wslc")]
    [InlineData("a b", "\"a b\"")]
    [InlineData("", "\"\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData("C:\\dir with space\\", "\"C:\\dir with space\\\\\"")]
    public void QuoteArgument_FollowsWindowsRules(string input, string expected) =>
        Assert.Equal(expected, TerminalProtocol.QuoteArgument(input));

    [Fact]
    public void ExecShellArguments_RequestsTtyAndTerminalType()
    {
        var args = TerminalProtocol.ExecShellArguments("WslcDesktop", "web-nginx", "sh");
        Assert.Contains("--tty", args);
        Assert.Contains("TERM=xterm-256color", args);
        Assert.Equal(["web-nginx", "sh"], args[^2..]);
        Assert.Equal("wslc --session WslcDesktop exec --interactive --tty -e TERM=xterm-256color -e COLORTERM=truecolor web-nginx sh",
            TerminalProtocol.BuildCommandLine("wslc", args));
    }

    [Fact]
    public void Input_EncodesUtf8()
    {
        Assert.Equal([0xC3, 0xA9], TerminalProtocol.EncodeInput("é"));
    }

    [Fact]
    public void Palettes_CoverAnsiColoursAndDifferByTheme()
    {
        var dark = TerminalProtocol.Palette(true);
        var light = TerminalProtocol.Palette(false);
        Assert.Equal(dark.Keys.Order(), light.Keys.Order());
        Assert.Equal(16 + 5, dark.Count);
        Assert.NotEqual(dark["background"], light["background"]);
    }

    [Fact]
    public async Task ConPty_RunsProcessAndReportsOutputAndExit()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var output = new StringBuilder();
        var exited = new TaskCompletionSource<int>();
        using var session = ConPtySession.Start(
            "cmd.exe /c \"echo conpty-works\"",
            100, 40,
            (buffer, count) => { lock (output) { output.Append(Encoding.UTF8.GetString(buffer, 0, count)); } },
            code => exited.TrySetResult(code));
        session.Resize(80, 24);

        var exitCode = await exited.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(0, exitCode);
        string text;
        lock (output)
        {
            text = output.ToString();
        }

        Assert.Contains("conpty-works", text);
    }

    [Fact]
    public async Task ConPty_ForwardsInputToProcess()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var output = new StringBuilder();
        var exited = new TaskCompletionSource<int>();
        using var session = ConPtySession.Start(
            "cmd.exe",
            100, 30,
            (buffer, count) => { lock (output) { output.Append(Encoding.UTF8.GetString(buffer, 0, count)); } },
            code => exited.TrySetResult(code));
        session.Write("echo from-input-42\rexit 7\r"u8);

        Assert.Equal(7, await exited.Task.WaitAsync(TimeSpan.FromSeconds(20)));
        lock (output)
        {
            Assert.Contains("from-input-42", output.ToString());
        }
    }

    [Fact]
    public void ConPty_MissingCommandThrows() =>
        Assert.Throws<InvalidOperationException>(() => ConPtySession.Start("definitely-not-a-command-xyz", 80, 24, (_, _) => { }, _ => { }));
}
