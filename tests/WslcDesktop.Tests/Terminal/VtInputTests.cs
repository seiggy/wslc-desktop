
using WslcDesktop.Terminal;

namespace WslcDesktop.Tests.Terminal;

public class VtInputTests
{
    [Fact]
    public void ArrowsRespectApplicationMode()
    {
        Assert.Equal("\u001b[A", VtInput.EncodeKey(38, false, false, false, false));
        Assert.Equal("\u001bOA", VtInput.EncodeKey(38, false, false, false, true));
    }

    [Fact]
    public void CtrlLettersAreControlCodes()
    {
        Assert.Equal("\u0003", VtInput.EncodeKey(67, true, false, false, false));
        Assert.Equal("\u0004", VtInput.EncodeKey(68, true, false, false, false));
    }

    [Fact]
    public void TabEnterBackspaceEscape()
    {
        Assert.Equal("\t", VtInput.EncodeKey(9, false, false, false, false));
        Assert.Equal("\r", VtInput.EncodeKey(13, false, false, false, false));
        Assert.Equal("\u001b", VtInput.EncodeKey(27, false, false, false, false));
        Assert.Equal("\u001b[Z", VtInput.EncodeKey(9, false, true, false, false));
    }

    [Fact]
    public void PlainLettersAreLeftToCharacterInput() =>
        Assert.Null(VtInput.EncodeKey(65, false, false, false, false));

    [Fact]
    public void BracketedPasteWraps()
    {
        Assert.Equal("\u001b[200~x\u001b[201~", VtInput.EncodePaste("x", true));
        Assert.Equal("x", VtInput.EncodePaste("x", false));
    }

    [Fact]
    public void SgrMouseReport() =>
        Assert.Equal("\u001b[<0;3;2M", VtInput.EncodeMouse(0, 2, 1, false, false, true));
}
