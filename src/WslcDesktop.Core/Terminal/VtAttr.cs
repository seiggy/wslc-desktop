using System.Text;

namespace WslcDesktop.Terminal;

[Flags]
public enum VtAttr : ushort
{
    None = 0,
    Bold = 1,
    Dim = 2,
    Italic = 4,
    Underline = 8,
    Inverse = 16,
    Strike = 32,
    Hidden = 64,
}
