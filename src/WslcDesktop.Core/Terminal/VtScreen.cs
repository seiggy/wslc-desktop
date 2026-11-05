using System.Text;

namespace WslcDesktop.Terminal;

/// <summary>
/// A UI-free terminal emulator: a VT/xterm parser feeding a cell grid with scrollback. It implements the subset that
/// Windows' pseudoconsole (ConPTY) emits and that common TUIs (vi, top, less, htop) use. Not thread-safe; callers serialise access.
/// </summary>
public sealed class VtScreen
{
    private enum State { Ground, Escape, EscapeIntermediate, Csi, Osc, OscEscape, IgnoreString, IgnoreStringEscape }

    private const int RgbFlag = 0x01000000;

    private readonly int _scrollbackLimit;
    private readonly List<VtCell[]> _scrollback = [];
    private readonly List<int> _args = [];
    private readonly StringBuilder _param = new();
    private readonly StringBuilder _osc = new();
    private VtCell[][] _main;
    private VtCell[][] _alt;
    private VtCell[][] _grid;
    private State _state;
    private char _csiPrefix;
    private char _intermediate;
    private char _highSurrogate;
    private int _top, _bottom;
    private bool _pendingWrap;
    private bool _autoWrap = true;
    private bool _insertMode;
    private bool _originMode;
    private bool _graphics;
    private int _fg = -1, _bg = -1;
    private VtAttr _attr;
    private (int X, int Y, int Fg, int Bg, VtAttr Attr, bool Wrap, bool Graphics) _saved;
    private (int X, int Y) _altSavedCursor;
    private char _lastPrinted;

    public VtScreen(int columns, int rows, int scrollbackLimit = 5000)
    {
        Columns = Math.Max(1, columns);
        Rows = Math.Max(1, rows);
        _scrollbackLimit = scrollbackLimit;
        _main = NewGrid(Columns, Rows);
        _alt = NewGrid(Columns, Rows);
        _grid = _main;
        _bottom = Rows - 1;
        _saved = (0, 0, -1, -1, VtAttr.None, false, false);
    }

    public int Columns { get; private set; }

    public int Rows { get; private set; }

    public int CursorX { get; private set; }

    public int CursorY { get; private set; }

    public bool CursorVisible { get; private set; } = true;

    public bool AppCursorKeys { get; private set; }

    public bool BracketedPaste { get; private set; }

    public bool AltScreen { get; private set; }

    /// <summary>0 = off, 1000 = clicks, 1002 = drag, 1003 = any motion.</summary>
    public int MouseMode { get; private set; }

    public bool MouseSgr { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public int ScrollbackCount => _scrollback.Count;

    /// <summary>Incremented on every change; renderers use it to skip redundant draws.</summary>
    public long Version { get; private set; }

    /// <summary>Raised with bytes the terminal must send back to the application (cursor reports, device attributes).</summary>
    public event Action<string>? Reply;

    public static int Rgb(int r, int g, int b) => RgbFlag | (r << 16) | (g << 8) | b;

    public static bool IsRgb(int color) => (color & RgbFlag) != 0;

    /// <summary>A line addressed from the oldest scrollback line (0) to the last visible row.</summary>
    public VtCell[] GetLine(int absolute)
    {
        if (absolute < _scrollback.Count)
        {
            return _scrollback[Math.Max(0, absolute)];
        }

        return _grid[Math.Clamp(absolute - _scrollback.Count, 0, Rows - 1)];
    }

    public int TotalLines => _scrollback.Count + Rows;

    public string GetText(int startLine, int startCol, int endLine, int endCol)
    {
        if (startLine > endLine || (startLine == endLine && startCol > endCol))
        {
            (startLine, startCol, endLine, endCol) = (endLine, endCol, startLine, startCol);
        }

        var sb = new StringBuilder();
        for (var line = Math.Max(0, startLine); line <= Math.Min(endLine, TotalLines - 1); line++)
        {
            var cells = GetLine(line);
            var from = line == startLine ? Math.Max(0, startCol) : 0;
            var to = line == endLine ? Math.Min(endCol, cells.Length - 1) : cells.Length - 1;
            var row = new StringBuilder();
            for (var x = from; x <= to; x++)
            {
                if (cells[x].Width != 0)
                {
                    row.Append(char.ConvertFromUtf32(cells[x].Cp is > 0 and <= 0x10FFFF and not (>= 0xD800 and <= 0xDFFF) ? cells[x].Cp : ' '));
                }
            }

            sb.Append(row.ToString().TrimEnd()).Append(line < endLine ? "\n" : string.Empty);
        }

        return sb.ToString();
    }

    public void Resize(int columns, int rows)
    {
        columns = Math.Max(1, columns);
        rows = Math.Max(1, rows);
        if (columns == Columns && rows == Rows)
        {
            return;
        }

        _main = ResizeGrid(_main, columns, rows, !AltScreen);
        _alt = ResizeGrid(_alt, columns, rows, false);
        _grid = AltScreen ? _alt : _main;
        Columns = columns;
        Rows = rows;
        _top = 0;
        _bottom = rows - 1;
        CursorX = Math.Min(CursorX, columns - 1);
        CursorY = Math.Min(CursorY, rows - 1);
        _pendingWrap = false;
        Version++;
    }

    public void Feed(ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            Process(c);
        }

        Version++;
    }

    public void Feed(string text) => Feed(text.AsSpan());

    private static VtCell[][] NewGrid(int columns, int rows)
    {
        var grid = new VtCell[rows][];
        for (var i = 0; i < rows; i++)
        {
            grid[i] = BlankLine(columns, -1);
        }

        return grid;
    }

    private static VtCell[] BlankLine(int columns, int bg)
    {
        var line = new VtCell[columns];
        Array.Fill(line, new VtCell { Cp = ' ', Fg = -1, Bg = bg, Width = 1 });
        return line;
    }

    private VtCell[][] ResizeGrid(VtCell[][] old, int columns, int rows, bool useScrollback)
    {
        var grid = new VtCell[rows][];
        var dropped = Math.Max(0, old.Length - rows);
        if (useScrollback)
        {
            // Shrinking pushes the top rows into scrollback so nothing the user saw is lost.
            for (var i = 0; i < dropped; i++)
            {
                PushScrollback(old[i]);
            }
        }

        for (var y = 0; y < rows; y++)
        {
            var source = y + dropped;
            grid[y] = source < old.Length ? FitLine(old[source], columns) : BlankLine(columns, -1);
        }

        if (useScrollback)
        {
            CursorY = Math.Max(0, CursorY - dropped);
        }

        return grid;
    }

    private static VtCell[] FitLine(VtCell[] line, int columns)
    {
        if (line.Length == columns)
        {
            return line;
        }

        var fitted = BlankLine(columns, -1);
        Array.Copy(line, fitted, Math.Min(line.Length, columns));
        return fitted;
    }

    private void PushScrollback(VtCell[] line)
    {
        _scrollback.Add(line);
        if (_scrollback.Count > _scrollbackLimit)
        {
            _scrollback.RemoveRange(0, _scrollback.Count - _scrollbackLimit);
        }
    }

    private void Process(char c)
    {
        switch (_state)
        {
            case State.Ground:
                Ground(c);
                break;
            case State.Escape:
                EscapeChar(c);
                break;
            case State.EscapeIntermediate:
                EscapeIntermediate(c);
                break;
            case State.Csi:
                CsiChar(c);
                break;
            case State.Osc:
                if (c == '\a')
                {
                    EndOsc();
                }
                else if (c == '\u001b')
                {
                    _state = State.OscEscape;
                }
                else
                {
                    _osc.Append(c);
                }

                break;
            case State.OscEscape:
                if (c == '\\')
                {
                    EndOsc();
                }
                else
                {
                    _state = State.Ground;
                }

                break;
            case State.IgnoreString:
                if (c == '\a')
                {
                    _state = State.Ground;
                }
                else if (c == '\u001b')
                {
                    _state = State.IgnoreStringEscape;
                }

                break;
            case State.IgnoreStringEscape:
                _state = c == '\\' ? State.Ground : State.IgnoreString;
                break;
        }
    }

    private void Ground(char c)
    {
        if (c < 0x20 || c == 0x7f)
        {
            Control(c);
            return;
        }

        if (char.IsHighSurrogate(c))
        {
            _highSurrogate = c;
            return;
        }

        int cp = c;
        if (char.IsLowSurrogate(c))
        {
            if (_highSurrogate == 0)
            {
                return;
            }

            cp = char.ConvertToUtf32(_highSurrogate, c);
            _highSurrogate = '\0';
        }

        Print(cp);
    }

    private void Control(char c)
    {
        switch (c)
        {
            case '\u001b':
                _state = State.Escape;
                break;
            case '\b':
                if (CursorX > 0)
                {
                    CursorX--;
                }

                _pendingWrap = false;
                break;
            case '\t':
                CursorX = Math.Min(Columns - 1, (CursorX / 8 + 1) * 8);
                _pendingWrap = false;
                break;
            case '\n' or '\v' or '\f':
                LineFeed();
                break;
            case '\r':
                CursorX = 0;
                _pendingWrap = false;
                break;
            case '\u000e':
                _graphics = true;
                break;
            case '\u000f':
                _graphics = false;
                break;
        }
    }

    private void EscapeChar(char c)
    {
        _state = State.Ground;
        switch (c)
        {
            case '[':
                _state = State.Csi;
                _args.Clear();
                _param.Clear();
                _csiPrefix = '\0';
                _intermediate = '\0';
                break;
            case ']':
                _state = State.Osc;
                _osc.Clear();
                break;
            case 'P' or 'X' or '^' or '_':
                _state = State.IgnoreString;
                break;
            case '(' or ')' or '*' or '+' or '#' or ' ' or '%':
                _intermediate = c;
                _state = State.EscapeIntermediate;
                break;
            case '7':
                SaveCursor();
                break;
            case '8':
                RestoreCursor();
                break;
            case 'D':
                LineFeed();
                break;
            case 'E':
                CursorX = 0;
                LineFeed();
                break;
            case 'M':
                ReverseIndex();
                break;
            case 'c':
                FullReset();
                break;
            case '\u001b':
                _state = State.Escape;
                break;
        }
    }

    private void EscapeIntermediate(char c)
    {
        _state = State.Ground;
        if (_intermediate == '(')
        {
            _graphics = c == '0';
        }
        else if (_intermediate == '#' && c == '8')
        {
            for (var y = 0; y < Rows; y++)
            {
                _grid[y] = FilledLine('E');
            }
        }
    }

    private VtCell[] FilledLine(char ch)
    {
        var line = BlankLine(Columns, -1);
        for (var x = 0; x < Columns; x++)
        {
            line[x].Cp = ch;
        }

        return line;
    }

    private void CsiChar(char c)
    {
        if (c < 0x20)
        {
            Control(c);
            return;
        }

        if (c is >= '0' and <= '?')
        {
            if (c is '<' or '=' or '>' or '?' && _param.Length == 0)
            {
                _csiPrefix = c;
            }
            else
            {
                _param.Append(c);
            }

            return;
        }

        if (c is >= ' ' and <= '/')
        {
            _intermediate = c;
            return;
        }

        _state = State.Ground;
        if (c is >= '@' and <= '~')
        {
            ParseArgs();
            ExecuteCsi(c);
        }
    }

    private void ParseArgs()
    {
        _args.Clear();
        var text = _param.ToString();
        if (text.Length == 0)
        {
            return;
        }

        foreach (var part in text.Split(';', ':'))
        {
            _args.Add(int.TryParse(part, out var value) ? Math.Min(value, 65535) : -1);
        }
    }

    private int Arg(int index, int fallback)
    {
        if (index < _args.Count && _args[index] > 0)
        {
            return _args[index];
        }

        return fallback;
    }

    private void ExecuteCsi(char final)
    {
        if (final is not ('m' or 'h' or 'l'))
        {
            _pendingWrap = false;
        }

        if (_csiPrefix == '?')
        {
            if (final is 'h' or 'l')
            {
                foreach (var mode in _args)
                {
                    SetPrivateMode(mode, final == 'h');
                }
            }

            return;
        }

        if (_csiPrefix == '>')
        {
            if (final == 'c')
            {
                Reply?.Invoke("\u001b[>0;0;0c");
            }

            return;
        }

        if (_csiPrefix != '\0' || _intermediate != '\0')
        {
            return;
        }

        switch (final)
        {
            case 'A': CursorY = Math.Max(_top <= CursorY ? _top : 0, CursorY - Arg(0, 1)); break;
            case 'B': CursorY = Math.Min(_bottom >= CursorY ? _bottom : Rows - 1, CursorY + Arg(0, 1)); break;
            case 'C' or 'a': CursorX = Math.Min(Columns - 1, CursorX + Arg(0, 1)); break;
            case 'D': CursorX = Math.Max(0, CursorX - Arg(0, 1)); break;
            case 'E': CursorX = 0; CursorY = Math.Min(Rows - 1, CursorY + Arg(0, 1)); break;
            case 'F': CursorX = 0; CursorY = Math.Max(0, CursorY - Arg(0, 1)); break;
            case 'G' or '`': CursorX = Math.Clamp(Arg(0, 1) - 1, 0, Columns - 1); break;
            case 'H' or 'f': MoveTo(Arg(0, 1) - 1, Arg(1, 1) - 1); break;
            case 'd': MoveTo(Arg(0, 1) - 1, CursorX, rowOnly: true); break;
            case 'J': EraseDisplay(_args.Count == 0 || _args[0] < 0 ? 0 : _args[0]); break;
            case 'K': EraseLine(_args.Count == 0 || _args[0] < 0 ? 0 : _args[0]); break;
            case 'L': InsertLines(Arg(0, 1)); break;
            case 'M': DeleteLines(Arg(0, 1)); break;
            case '@': InsertChars(Arg(0, 1)); break;
            case 'P': DeleteChars(Arg(0, 1)); break;
            case 'X': EraseChars(Arg(0, 1)); break;
            case 'S': ScrollUp(Arg(0, 1)); break;
            case 'T': ScrollDown(Arg(0, 1)); break;
            case 'b': RepeatLast(Arg(0, 1)); break;
            case 'm': Sgr(); break;
            case 'r': SetScrollRegion(); break;
            case 's': SaveCursor(); break;
            case 'u': RestoreCursor(); break;
            case 'h' or 'l':
                if (_args.Contains(4))
                {
                    _insertMode = final == 'h';
                }

                break;
            case 'n':
                if (Arg(0, 0) == 5)
                {
                    Reply?.Invoke("\u001b[0n");
                }
                else if (Arg(0, 0) == 6)
                {
                    Reply?.Invoke($"\u001b[{CursorY + 1 - (_originMode ? _top : 0)};{CursorX + 1}R");
                }

                break;
            case 'c':
                Reply?.Invoke("\u001b[?1;2c");
                break;
        }
    }

    private void SetPrivateMode(int mode, bool on)
    {
        switch (mode)
        {
            case 1: AppCursorKeys = on; break;
            case 6: _originMode = on; MoveTo(0, 0); break;
            case 7: _autoWrap = on; break;
            case 25: CursorVisible = on; break;
            case 1000 or 1002 or 1003: MouseMode = on ? mode : 0; break;
            case 1006: MouseSgr = on; break;
            case 2004: BracketedPaste = on; break;
            case 47 or 1047: SwitchScreen(on, saveCursor: false); break;
            case 1048:
                if (on) { SaveCursor(); } else { RestoreCursor(); }

                break;
            case 1049: SwitchScreen(on, saveCursor: true); break;
        }
    }

    private void SwitchScreen(bool alt, bool saveCursor)
    {
        if (alt == AltScreen)
        {
            return;
        }

        AltScreen = alt;
        if (alt)
        {
            if (saveCursor)
            {
                _altSavedCursor = (CursorX, CursorY);
                SaveCursor();
            }

            _alt = NewGrid(Columns, Rows);
            _grid = _alt;
        }
        else
        {
            _grid = _main;
            if (saveCursor)
            {
                RestoreCursor();
                (CursorX, CursorY) = (Math.Min(_altSavedCursor.X, Columns - 1), Math.Min(_altSavedCursor.Y, Rows - 1));
            }
        }

        _top = 0;
        _bottom = Rows - 1;
        _pendingWrap = false;
    }

    private void MoveTo(int row, int col, bool rowOnly = false)
    {
        var offset = _originMode ? _top : 0;
        var max = _originMode ? _bottom : Rows - 1;
        CursorY = Math.Clamp(row + offset, offset, max);
        if (!rowOnly)
        {
            CursorX = Math.Clamp(col, 0, Columns - 1);
        }
    }

    private void SetScrollRegion()
    {
        var top = Arg(0, 1) - 1;
        var bottom = Arg(1, Rows) - 1;
        if (top < bottom && bottom < Rows)
        {
            (_top, _bottom) = (top, bottom);
            MoveTo(0, 0);
        }
    }

    private void SaveCursor() => _saved = (CursorX, CursorY, _fg, _bg, _attr, _pendingWrap, _graphics);

    private void RestoreCursor()
    {
        (CursorX, CursorY, _fg, _bg, _attr, _pendingWrap, _graphics) = _saved;
        CursorX = Math.Min(CursorX, Columns - 1);
        CursorY = Math.Min(CursorY, Rows - 1);
    }

    private void FullReset()
    {
        _main = NewGrid(Columns, Rows);
        _alt = NewGrid(Columns, Rows);
        _grid = _main;
        AltScreen = false;
        (CursorX, CursorY, _top, _bottom) = (0, 0, 0, Rows - 1);
        (_fg, _bg, _attr) = (-1, -1, VtAttr.None);
        (_pendingWrap, _autoWrap, _insertMode, _originMode, _graphics) = (false, true, false, false, false);
        (CursorVisible, AppCursorKeys, BracketedPaste, MouseMode, MouseSgr) = (true, false, false, 0, false);
        _saved = (0, 0, -1, -1, VtAttr.None, false, false);
    }

    private void EndOsc()
    {
        _state = State.Ground;
        var text = _osc.ToString();
        var sep = text.IndexOf(';');
        if (sep > 0 && text[..sep] is "0" or "2")
        {
            Title = text[(sep + 1)..];
        }
    }

    private void Sgr()
    {
        if (_args.Count == 0)
        {
            (_fg, _bg, _attr) = (-1, -1, VtAttr.None);
            return;
        }

        for (var i = 0; i < _args.Count; i++)
        {
            var p = _args[i] < 0 ? 0 : _args[i];
            switch (p)
            {
                case 0: (_fg, _bg, _attr) = (-1, -1, VtAttr.None); break;
                case 1: _attr |= VtAttr.Bold; break;
                case 2: _attr |= VtAttr.Dim; break;
                case 3: _attr |= VtAttr.Italic; break;
                case 4: _attr |= VtAttr.Underline; break;
                case 7: _attr |= VtAttr.Inverse; break;
                case 8: _attr |= VtAttr.Hidden; break;
                case 9: _attr |= VtAttr.Strike; break;
                case 21 or 24: _attr &= ~VtAttr.Underline; break;
                case 22: _attr &= ~(VtAttr.Bold | VtAttr.Dim); break;
                case 23: _attr &= ~VtAttr.Italic; break;
                case 27: _attr &= ~VtAttr.Inverse; break;
                case 28: _attr &= ~VtAttr.Hidden; break;
                case 29: _attr &= ~VtAttr.Strike; break;
                case >= 30 and <= 37: _fg = p - 30; break;
                case 39: _fg = -1; break;
                case >= 40 and <= 47: _bg = p - 40; break;
                case 49: _bg = -1; break;
                case >= 90 and <= 97: _fg = p - 90 + 8; break;
                case >= 100 and <= 107: _bg = p - 100 + 8; break;
                case 38 or 48:
                    var color = ExtendedColor(ref i);
                    if (color != int.MinValue)
                    {
                        if (p == 38) { _fg = color; } else { _bg = color; }
                    }

                    break;
            }
        }
    }

    private int ExtendedColor(ref int i)
    {
        var mode = i + 1 < _args.Count ? _args[i + 1] : -1;
        if (mode == 5 && i + 2 < _args.Count)
        {
            var index = _args[i + 2];
            i += 2;
            return index is >= 0 and <= 255 ? index : int.MinValue;
        }

        if (mode == 2 && i + 4 < _args.Count)
        {
            // Colour-space id forms (38;2;;r;g;b) carry one extra parameter.
            var offset = i + 5 < _args.Count && _args[i + 2] < 0 ? 3 : 2;
            if (i + offset + 2 < _args.Count)
            {
                var color = Rgb(Math.Clamp(_args[i + offset], 0, 255), Math.Clamp(_args[i + offset + 1], 0, 255), Math.Clamp(_args[i + offset + 2], 0, 255));
                i += offset + 2;
                return color;
            }
        }

        i = _args.Count;
        return int.MinValue;
    }

    private void RepeatLast(int count)
    {
        if (_lastPrinted == '\0')
        {
            return;
        }

        for (var i = 0; i < Math.Min(count, 4096); i++)
        {
            Print(_lastPrinted);
        }
    }

    private static readonly Dictionary<int, int> LineDrawing = new()
    {
        ['`'] = 0x25C6, ['a'] = 0x2592, ['f'] = 0xB0, ['g'] = 0xB1, ['j'] = 0x2518, ['k'] = 0x2510, ['l'] = 0x250C, ['m'] = 0x2514,
        ['n'] = 0x253C, ['o'] = 0x23BA, ['p'] = 0x23BB, ['q'] = 0x2500, ['r'] = 0x23BC, ['s'] = 0x23BD, ['t'] = 0x251C,
        ['u'] = 0x2524, ['v'] = 0x2534, ['w'] = 0x252C, ['x'] = 0x2502, ['y'] = 0x2264, ['z'] = 0x2265, ['{'] = 0x3C0,
        ['|'] = 0x2260, ['}'] = 0xA3, ['~'] = 0xB7,
    };

    /// <summary>Terminal cell width of a code point: 0 for combining marks, 2 for East Asian wide and emoji, otherwise 1.</summary>
    public static int CharWidth(int cp) => cp switch
    {
        >= 0x0300 and <= 0x036F or >= 0x200B and <= 0x200F or >= 0x20D0 and <= 0x20FF or >= 0xFE00 and <= 0xFE0F => 0,
        >= 0x1100 and <= 0x115F or >= 0x2E80 and <= 0xA4CF or >= 0xAC00 and <= 0xD7A3 or >= 0xF900 and <= 0xFAFF
            or >= 0xFE30 and <= 0xFE6F or >= 0xFF00 and <= 0xFF60 or >= 0xFFE0 and <= 0xFFE6
            or >= 0x1F300 and <= 0x1F64F or >= 0x1F900 and <= 0x1F9FF or >= 0x20000 and <= 0x3FFFD => 2,
        _ => 1,
    };

    private void Print(int cp)
    {
        if (_graphics && LineDrawing.TryGetValue(cp, out var mapped))
        {
            cp = mapped;
        }

        var width = CharWidth(cp);
        if (width == 0)
        {
            return;
        }

        _lastPrinted = cp < 0x10000 ? (char)cp : '\0';
        if (_pendingWrap && _autoWrap)
        {
            CursorX = 0;
            LineFeed();
        }

        _pendingWrap = false;
        if (width == 2 && CursorX == Columns - 1)
        {
            if (_autoWrap)
            {
                _grid[CursorY][CursorX] = Blank();
                CursorX = 0;
                LineFeed();
            }
            else
            {
                width = 1;
            }
        }

        var line = _grid[CursorY];
        if (_insertMode)
        {
            InsertChars(width);
        }

        ClearWideFragments(line, CursorX);
        if (width == 2)
        {
            ClearWideFragments(line, CursorX + 1);
        }

        line[CursorX] = new VtCell { Cp = cp, Fg = _fg, Bg = _bg, Attr = _attr, Width = (byte)width };
        if (width == 2)
        {
            line[CursorX + 1] = new VtCell { Cp = ' ', Fg = _fg, Bg = _bg, Attr = _attr, Width = 0 };
        }

        CursorX += width;
        if (CursorX >= Columns)
        {
            CursorX = Columns - 1;
            _pendingWrap = true;
        }
    }

    private static void ClearWideFragments(VtCell[] line, int x)
    {
        if (x >= line.Length)
        {
            return;
        }

        if (line[x].Width == 0 && x > 0)
        {
            line[x - 1].Cp = ' ';
            line[x - 1].Width = 1;
        }
        else if (line[x].Width == 2 && x + 1 < line.Length)
        {
            line[x + 1].Cp = ' ';
            line[x + 1].Width = 1;
        }
    }

    private VtCell Blank() => new() { Cp = ' ', Fg = -1, Bg = _bg, Width = 1 };

    private void LineFeed()
    {
        if (CursorY == _bottom)
        {
            ScrollUp(1);
        }
        else if (CursorY < Rows - 1)
        {
            CursorY++;
        }
    }

    private void ReverseIndex()
    {
        if (CursorY == _top)
        {
            ScrollDown(1);
        }
        else if (CursorY > 0)
        {
            CursorY--;
        }
    }

    private void ScrollUp(int count, bool toScrollback = true)
    {
        count = Math.Min(count, _bottom - _top + 1);
        for (var n = 0; n < count; n++)
        {
            if (toScrollback && _top == 0 && !AltScreen)
            {
                PushScrollback(_grid[0]);
            }

            for (var y = _top; y < _bottom; y++)
            {
                _grid[y] = _grid[y + 1];
            }

            _grid[_bottom] = BlankLine(Columns, _bg);
        }
    }

    private void ScrollDown(int count)
    {
        count = Math.Min(count, _bottom - _top + 1);
        for (var n = 0; n < count; n++)
        {
            for (var y = _bottom; y > _top; y--)
            {
                _grid[y] = _grid[y - 1];
            }

            _grid[_top] = BlankLine(Columns, _bg);
        }
    }

    private void InsertLines(int count)
    {
        if (CursorY < _top || CursorY > _bottom)
        {
            return;
        }

        var saved = _top;
        _top = CursorY;
        ScrollDown(count);
        _top = saved;
        CursorX = 0;
    }

    private void DeleteLines(int count)
    {
        if (CursorY < _top || CursorY > _bottom)
        {
            return;
        }

        var saved = _top;
        _top = CursorY;
        ScrollUp(count, toScrollback: false);
        _top = saved;
        CursorX = 0;
    }

    private void InsertChars(int count)
    {
        var line = _grid[CursorY];
        count = Math.Min(count, Columns - CursorX);
        Array.Copy(line, CursorX, line, CursorX + count, Columns - CursorX - count);
        for (var i = 0; i < count; i++)
        {
            line[CursorX + i] = Blank();
        }
    }

    private void DeleteChars(int count)
    {
        var line = _grid[CursorY];
        count = Math.Min(count, Columns - CursorX);
        Array.Copy(line, CursorX + count, line, CursorX, Columns - CursorX - count);
        for (var i = Columns - count; i < Columns; i++)
        {
            line[i] = Blank();
        }
    }

    private void EraseChars(int count)
    {
        var line = _grid[CursorY];
        for (var x = CursorX; x < Math.Min(Columns, CursorX + count); x++)
        {
            line[x] = Blank();
        }
    }

    private void EraseLine(int mode)
    {
        var line = _grid[CursorY];
        var (from, to) = mode switch
        {
            0 => (CursorX, Columns - 1),
            1 => (0, CursorX),
            _ => (0, Columns - 1),
        };
        for (var x = from; x <= to; x++)
        {
            line[x] = Blank();
        }
    }

    private void EraseDisplay(int mode)
    {
        switch (mode)
        {
            case 0:
                EraseLine(0);
                for (var y = CursorY + 1; y < Rows; y++)
                {
                    _grid[y] = BlankLine(Columns, _bg);
                }

                break;
            case 1:
                EraseLine(1);
                for (var y = 0; y < CursorY; y++)
                {
                    _grid[y] = BlankLine(Columns, _bg);
                }

                break;
            case 2:
                for (var y = 0; y < Rows; y++)
                {
                    _grid[y] = BlankLine(Columns, _bg);
                }

                break;
            case 3:
                _scrollback.Clear();
                break;
        }
    }
}
