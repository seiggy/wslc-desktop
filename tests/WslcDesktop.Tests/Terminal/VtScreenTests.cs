
using WslcDesktop.Terminal;

namespace WslcDesktop.Tests.Terminal;

public class VtScreenTests
{
    private static string Row(VtScreen s, int row, int cols = 10) =>
        string.Concat(Enumerable.Range(0, cols).Select(x => (char)Math.Max(' ', s.GetLine(s.TotalLines - s.Rows + row)[x].Cp))).TrimEnd();

    [Fact]
    public void PlainTextAndNewline()
    {
        var s = new VtScreen(20, 5);
        s.Feed("hi\r\nyo");
        Assert.Equal("hi", Row(s, 0));
        Assert.Equal("yo", Row(s, 1));
        Assert.Equal((2, 1), (s.CursorX, s.CursorY));
    }

    [Fact]
    public void SgrSetsAndResetsColors()
    {
        var s = new VtScreen(20, 5);
        s.Feed("\u001b[1;34mbin\u001b[mx");
        var line = s.GetLine(s.TotalLines - s.Rows);
        Assert.Equal(4, line[0].Fg);
        Assert.True(line[0].Attr.HasFlag(VtAttr.Bold));
        Assert.Equal(-1, line[3].Fg);
        Assert.Equal('b', line[0].Cp);
    }

    [Fact]
    public void TrueColorIsRgb()
    {
        var s = new VtScreen(20, 5);
        s.Feed("\u001b[38;2;10;20;30mA");
        var fg = s.GetLine(s.TotalLines - s.Rows)[0].Fg;
        Assert.True(VtScreen.IsRgb(fg));
        Assert.Equal(0x0A141E, fg & 0xFFFFFF);
    }

    [Fact]
    public void CursorMovementAndErase()
    {
        var s = new VtScreen(20, 5);
        s.Feed("abcdef\u001b[3D\u001b[K");
        Assert.Equal("abc", Row(s, 0));
        s.Feed("\u001b[2;4HZ");
        Assert.Equal("   Z", Row(s, 1));
        s.Feed("\u001b[2J");
        Assert.Equal("", Row(s, 1));
    }

    [Fact]
    public void ScrollingFillsScrollback()
    {
        var s = new VtScreen(10, 3);
        s.Feed("1\r\n2\r\n3\r\n4\r\n5");
        Assert.Equal(2, s.ScrollbackCount);
        Assert.Equal("3", Row(s, 0));
        Assert.Equal("5", Row(s, 2));
    }

    [Fact]
    public void AltScreenRestoresMainBuffer()
    {
        var s = new VtScreen(10, 3);
        s.Feed("main\u001b[?1049hALT");
        Assert.True(s.AltScreen);
        Assert.Equal("ALT", Row(s, 0).Trim());
        s.Feed("\u001b[?1049l");
        Assert.False(s.AltScreen);
        Assert.Equal("main", Row(s, 0));
    }

    [Fact]
    public void WideCharactersTakeTwoCells()
    {
        var s = new VtScreen(10, 3);
        s.Feed("日a");
        var line = s.GetLine(s.TotalLines - s.Rows);
        Assert.Equal(2, line[0].Width);
        Assert.Equal(0, line[1].Width);
        Assert.Equal('a', line[2].Cp);
    }

    [Fact]
    public void CursorPositionReportReplies()
    {
        var s = new VtScreen(10, 5);
        string? reply = null;
        s.Reply += r => reply = r;
        s.Feed("\u001b[2;3H\u001b[6n");
        Assert.Equal("\u001b[2;3R", reply);
    }

    [Fact]
    public void ModesAndTitleAreTracked()
    {
        var s = new VtScreen(10, 5);
        s.Feed("\u001b[?1h\u001b[?2004h\u001b[?1000h\u001b[?1006h\u001b[?25l\u001b]0;hello\u0007");
        Assert.True(s.AppCursorKeys);
        Assert.True(s.BracketedPaste);
        Assert.Equal(1000, s.MouseMode);
        Assert.True(s.MouseSgr);
        Assert.False(s.CursorVisible);
        Assert.Equal("hello", s.Title);
    }

    [Fact]
    public void ResizeKeepsContent()
    {
        var s = new VtScreen(10, 3);
        s.Feed("abc");
        s.Resize(20, 6);
        Assert.Equal(20, s.Columns);
        Assert.Equal(6, s.Rows);
        Assert.Equal("abc", Row(s, 0));
    }

    [Fact]
    public void SplitEscapeSequenceAcrossFeeds()
    {
        var s = new VtScreen(10, 3);
        s.Feed("\u001b[3");
        s.Feed("1mR");
        Assert.Equal(1, s.GetLine(s.TotalLines - s.Rows)[0].Fg);
    }

    [Fact]
    public void EmptyCsiParametersKeepTheirDefaultMeaning()
    {
        var s = new VtScreen(10, 3);
        s.Feed("\u001b[;2H");

        Assert.Equal((1, 0), (s.CursorX, s.CursorY));
    }

    [Fact]
    public void ScrollbackLimitRetainsTheNewestLinesInOrder()
    {
        var s = new VtScreen(10, 2, scrollbackLimit: 3);
        s.Feed("1\r\n2\r\n3\r\n4\r\n5\r\n6");

        Assert.Equal(3, s.ScrollbackCount);
        Assert.Equal("2", string.Concat(s.GetLine(0).Select(c => (char)c.Cp)).TrimEnd());
        Assert.Equal("5", string.Concat(s.GetLine(s.TotalLines - s.Rows).Select(c => (char)c.Cp)).TrimEnd());
        Assert.Equal("6", string.Concat(s.GetLine(s.TotalLines - 1).Select(c => (char)c.Cp)).TrimEnd());
    }
}
