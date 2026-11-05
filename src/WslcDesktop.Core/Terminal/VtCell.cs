using System.Text;

namespace WslcDesktop.Terminal;

/// <summary>One screen cell. Colours are -1 (default), 0-255 (palette index) or <see cref="VtScreen.Rgb"/>.</summary>
public struct VtCell
{
    public int Cp;
    public int Fg;
    public int Bg;
    public VtAttr Attr;

    /// <summary>1 for normal cells, 2 for the left half of a wide character, 0 for its right half.</summary>
    public byte Width;
}
