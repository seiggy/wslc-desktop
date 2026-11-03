using System.Diagnostics;
using System.Text;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI;
using WslcDesktop.Platform;
using WslcDesktop.Services;

namespace WslcDesktop.Controls;

/// <summary>
/// A terminal drawn natively in the XAML tree (Win2D): a Windows pseudoconsole (ConPTY) session feeds a managed VT
/// screen buffer (<see cref="VtScreen"/>). No child window or web view, so it never overlays other content.
/// <see cref="StartAsync"/> throws when rendering can't initialise so the caller can fall back to the simple shell.
/// </summary>
public sealed partial class TerminalView : UserControl
{
    private readonly object _outputGate = new();
    private readonly List<byte> _outputBuffer = [];
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
    private readonly Decoder _decoder = new UTF8Encoding(false).GetDecoder();
    private readonly Color[] _palette = new Color[16];
    private VtScreen _screen = new(TerminalProtocol.DefaultColumns, TerminalProtocol.DefaultRows);
    private ConPtySession? _session;
    private CanvasTextFormat _format = new();
    private CanvasTextFormat _boldFormat = new();
    private Color _defaultFg, _defaultBg, _selectionBg;
    private float _cellWidth = 8, _cellHeight = 16;
    private string _fontFamily = App.Host.Settings.TerminalFontFamily;
    private int _fontSize = App.Host.Settings.TerminalFontSize;
    private bool _ready, _flushQueued, _selecting, _mouseDown, _hasSelection;
    private int _scrollOffset;
    private (int Line, int Col) _selStart, _selEnd;
    private char _pendingHighSurrogate;

    public TerminalView()
    {
        InitializeComponent();
        ActualThemeChanged += (_, _) => { LoadPalette(); Surface.Invalidate(); };
        GotFocus += (_, _) => Surface.Invalidate();
        LostFocus += (_, _) => Surface.Invalidate();
        PreviewKeyDown += OnPreviewKeyDown;
        CharacterReceived += OnCharacterReceived;
        LoadPalette();
        AttachScreen(_screen);
    }

    public bool IsRunning => _session is not null;

    private bool IsDark => ActualTheme != ElementTheme.Light;

    public async Task StartAsync(string session, string container, string shell, Action<int> onExit)
    {
        if (!_ready)
        {
            throw new InvalidOperationException("The terminal drawing surface (Win2D) isn't available.");
        }

        Stop();
        _screen = new VtScreen(_screen.Columns, _screen.Rows);
        AttachScreen(_screen);
        (_scrollOffset, _hasSelection) = (0, false);
        Surface.Invalidate();

        var commandLine = TerminalProtocol.BuildCommandLine("wslc", TerminalProtocol.ExecShellArguments(session, container, shell));
        ConPtySession? started = null;
        started = ConPtySession.Start(commandLine, (short)_screen.Columns, (short)_screen.Rows, QueueOutput, code => _dispatcher.TryEnqueue(() =>
        {
            if (ReferenceEquals(_session, started))
            {
                _session = null;
                started?.Dispose();
                onExit(code);
            }
        }));
        _session = started;

        try
        {
            ChildProcessTracker.Track(Process.GetProcessById(started.ProcessId));
        }
        catch (ArgumentException)
        {
            // Already gone; the exit callback reports it.
        }

        FocusTerminal();
        await Task.CompletedTask;
    }

    public void Stop()
    {
        var session = _session;
        _session = null;
        session?.Dispose();
    }

    public void Notice(string text)
    {
        _screen.Feed($"\r\n\u001b[33m{text}\u001b[0m\r\n");
        Surface.Invalidate();
    }

    public void FocusTerminal() => Focus(FocusState.Programmatic);

    public void SetFont(string family, int size)
    {
        (_fontFamily, _fontSize) = (family, size);
        if (_ready)
        {
            MeasureFont();
            FitGrid();
            Surface.Invalidate();
        }
    }

    private void AttachScreen(VtScreen screen) =>
        screen.Reply += text => _session?.Write(Encoding.UTF8.GetBytes(text));

    private static Color Hex(string hex) => Color.FromArgb(
        255, Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16));

    private void LoadPalette()
    {
        var p = TerminalProtocol.Palette(IsDark);
        string[] order =
        [
            "black", "red", "green", "yellow", "blue", "magenta", "cyan", "white",
            "brightBlack", "brightRed", "brightGreen", "brightYellow", "brightBlue", "brightMagenta", "brightCyan", "brightWhite",
        ];
        for (var i = 0; i < 16; i++)
        {
            _palette[i] = Hex(p[order[i]]);
        }

        (_defaultFg, _defaultBg, _selectionBg) = (Hex(p["foreground"]), Hex(p["background"]), Hex(p["selectionBackground"]));
    }

    private void Surface_CreateResources(CanvasControl sender, Microsoft.Graphics.Canvas.UI.CanvasCreateResourcesEventArgs args)
    {
        _ready = true;
        MeasureFont();
        FitGrid();
    }

    private void MeasureFont()
    {
        _format = new CanvasTextFormat { FontFamily = _fontFamily, FontSize = _fontSize, WordWrapping = CanvasWordWrapping.NoWrap };
        _boldFormat = new CanvasTextFormat { FontFamily = _fontFamily, FontSize = _fontSize, FontWeight = Microsoft.UI.Text.FontWeights.Bold, WordWrapping = CanvasWordWrapping.NoWrap };
        using var layout = new CanvasTextLayout(CanvasDevice.GetSharedDevice(), new string('M', 10), _format, 10000, 1000);
        _cellWidth = (float)Math.Max(1, layout.LayoutBounds.Width / 10);
        _cellHeight = (float)Math.Max(1, layout.LayoutBounds.Height);
    }

    private void Surface_SizeChanged(object sender, SizeChangedEventArgs e) => FitGrid();

    private void FitGrid()
    {
        if (!_ready || Surface.ActualWidth < _cellWidth || Surface.ActualHeight < _cellHeight)
        {
            return;
        }

        var columns = TerminalProtocol.ClampDimension((int)(Surface.ActualWidth / _cellWidth));
        var rows = TerminalProtocol.ClampDimension((int)(Surface.ActualHeight / _cellHeight));
        if (columns > 0 && rows > 0 && (columns != _screen.Columns || rows != _screen.Rows))
        {
            _screen.Resize(columns, rows);
            _session?.Resize(columns, rows);
            _scrollOffset = Math.Min(_scrollOffset, _screen.ScrollbackCount);
        }

        Surface.Invalidate();
    }

    private void QueueOutput(byte[] data, int count)
    {
        lock (_outputGate)
        {
            _outputBuffer.AddRange(new ArraySegment<byte>(data, 0, count));
            if (_flushQueued)
            {
                return;
            }

            _flushQueued = true;
        }

        _dispatcher.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, FlushOutput);
    }

    private void FlushOutput()
    {
        byte[] bytes;
        lock (_outputGate)
        {
            bytes = [.. _outputBuffer];
            _outputBuffer.Clear();
            _flushQueued = false;
        }

        var chars = new char[_decoder.GetCharCount(bytes, 0, bytes.Length)];
        var n = _decoder.GetChars(bytes, 0, bytes.Length, chars, 0);
        var before = _screen.ScrollbackCount;
        _screen.Feed(chars.AsSpan(0, n));
        if (_scrollOffset > 0)
        {
            _scrollOffset = Math.Min(_screen.ScrollbackCount, _scrollOffset + _screen.ScrollbackCount - before);
        }

        Surface.Invalidate();
    }

    private Color Resolve(int color, bool bold, Color fallback)
    {
        if (color < 0)
        {
            return fallback;
        }

        if (VtScreen.IsRgb(color))
        {
            return Color.FromArgb(255, (byte)(color >> 16), (byte)(color >> 8), (byte)color);
        }

        if (color < 8 && bold)
        {
            color += 8;
        }

        if (color < 16)
        {
            return _palette[color];
        }

        if (color >= 232)
        {
            var g = (byte)(8 + (color - 232) * 10);
            return Color.FromArgb(255, g, g, g);
        }

        var i = color - 16;
        byte Level(int v) => (byte)(v == 0 ? 0 : 55 + v * 40);
        return Color.FromArgb(255, Level(i / 36), Level(i / 6 % 6), Level(i % 6));
    }

    private (Color Fg, Color Bg, bool BgDefault) Colors(in VtCell cell)
    {
        var bold = cell.Attr.HasFlag(VtAttr.Bold);
        var fg = Resolve(cell.Fg, bold, _defaultFg);
        var bg = Resolve(cell.Bg, false, _defaultBg);
        var bgDefault = cell.Bg < 0;
        if (cell.Attr.HasFlag(VtAttr.Inverse))
        {
            (fg, bg) = (bg, fg);
            bgDefault = false;
        }

        if (cell.Attr.HasFlag(VtAttr.Dim))
        {
            fg = Color.FromArgb(255, (byte)((fg.R + bg.R) / 2), (byte)((fg.G + bg.G) / 2), (byte)((fg.B + bg.B) / 2));
        }

        return (fg, bg, bgDefault);
    }

    private void Surface_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        ds.Clear(_defaultBg);
        var screen = _screen;
        var first = screen.TotalLines - screen.Rows - _scrollOffset;
        var (selFrom, selTo) = OrderedSelection();

        for (var row = 0; row < screen.Rows; row++)
        {
            var absolute = first + row;
            var line = screen.GetLine(absolute);
            var y = row * _cellHeight;
            var columns = Math.Min(line.Length, screen.Columns);

            for (var x = 0; x < columns; x++)
            {
                var selected = _hasSelection && IsSelected(absolute, x, selFrom, selTo);
                var (_, bg, bgDefault) = Colors(line[x]);
                if (selected)
                {
                    ds.FillRectangle(x * _cellWidth, y, _cellWidth, _cellHeight, _selectionBg);
                }
                else if (!bgDefault)
                {
                    ds.FillRectangle(x * _cellWidth, y, _cellWidth + 0.5f, _cellHeight, bg);
                }
            }

            DrawText(ds, line, columns, y);
        }

        DrawCursor(ds, screen, first);
        if (_scrollOffset > 0)
        {
            var track = screen.Rows * _cellHeight;
            var thumb = Math.Max(24, track * screen.Rows / screen.TotalLines);
            var top = (track - thumb) * (screen.TotalLines - screen.Rows - _scrollOffset) / Math.Max(1, screen.TotalLines - screen.Rows);
            var width = (float)Surface.ActualWidth;
            ds.FillRoundedRectangle(width - 6, top, 4, thumb, 2, 2, Color.FromArgb(120, _defaultFg.R, _defaultFg.G, _defaultFg.B));
        }
    }

    private void DrawText(CanvasDrawingSession ds, VtCell[] line, int columns, float y)
    {
        var run = new StringBuilder();
        var runStart = 0;
        VtCell runCell = default;

        void Flush()
        {
            if (run.Length > 0 && run.ToString().Any(ch => ch != ' ' || runCell.Attr.HasFlag(VtAttr.Underline)))
            {
                var (fg, _, _) = Colors(runCell);
                var x = runStart * _cellWidth;
                ds.DrawText(run.ToString(), x, y, fg, runCell.Attr.HasFlag(VtAttr.Bold) ? _boldFormat : _format);
                if (runCell.Attr.HasFlag(VtAttr.Underline))
                {
                    ds.DrawLine(x, y + _cellHeight - 1, x + run.Length * _cellWidth, y + _cellHeight - 1, fg);
                }

                if (runCell.Attr.HasFlag(VtAttr.Strike))
                {
                    ds.DrawLine(x, y + _cellHeight / 2, x + run.Length * _cellWidth, y + _cellHeight / 2, fg);
                }
            }

            run.Clear();
        }

        for (var x = 0; x < columns; x++)
        {
            var cell = line[x];
            if (cell.Width == 0 || cell.Attr.HasFlag(VtAttr.Hidden))
            {
                Flush();
                continue;
            }

            if (cell.Cp is < 0x20 or > 0x7E)
            {
                // Glyphs outside ASCII can have fallback-font widths, so each is placed on its own cell.
                Flush();
                var (fg, _, _) = Colors(cell);
                var text = char.ConvertFromUtf32(cell.Cp is > 0 and <= 0x10FFFF and not (>= 0xD800 and <= 0xDFFF) ? cell.Cp : '?');
                ds.DrawText(text, x * _cellWidth, y, fg, cell.Attr.HasFlag(VtAttr.Bold) ? _boldFormat : _format);
                continue;
            }

            var sameStyle = run.Length > 0 && cell.Fg == runCell.Fg && cell.Bg == runCell.Bg && cell.Attr == runCell.Attr;
            if (!sameStyle)
            {
                Flush();
                runStart = x;
                runCell = cell;
            }

            run.Append((char)cell.Cp);
        }

        Flush();
    }

    private void DrawCursor(CanvasDrawingSession ds, VtScreen screen, int first)
    {
        if (!screen.CursorVisible || _scrollOffset > 0)
        {
            return;
        }

        var x = screen.CursorX * _cellWidth;
        var y = screen.CursorY * _cellHeight;
        var color = Color.FromArgb(255, _defaultFg.R, _defaultFg.G, _defaultFg.B);
        if (FocusState != FocusState.Unfocused)
        {
            ds.FillRectangle(x, y + 1, 2, _cellHeight - 2, color);
        }
        else
        {
            ds.DrawRectangle(x + 0.5f, y + 1.5f, _cellWidth - 1, _cellHeight - 3, color);
        }
    }

    private ((int Line, int Col), (int Line, int Col)) OrderedSelection() =>
        (_selStart.Line, _selStart.Col).CompareTo((_selEnd.Line, _selEnd.Col)) <= 0 ? (_selStart, _selEnd) : (_selEnd, _selStart);

    private static bool IsSelected(int line, int col, (int Line, int Col) from, (int Line, int Col) to) =>
        (line, col).CompareTo((from.Line, from.Col)) >= 0 && (line, col).CompareTo((to.Line, to.Col)) <= 0;

    private (int Line, int Col) CellAt(Windows.Foundation.Point point)
    {
        var col = Math.Clamp((int)(point.X / _cellWidth), 0, _screen.Columns - 1);
        var row = Math.Clamp((int)(point.Y / _cellHeight), 0, _screen.Rows - 1);
        return (_screen.TotalLines - _screen.Rows - _scrollOffset + row, col);
    }

    private (int Col, int Row) ViewportCell(Windows.Foundation.Point point) =>
        (Math.Clamp((int)(point.X / _cellWidth), 0, _screen.Columns - 1), Math.Clamp((int)(point.Y / _cellHeight), 0, _screen.Rows - 1));

    private static bool KeyDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private void Send(string text) => _session?.Write(Encoding.UTF8.GetBytes(text));

    private void Surface_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Focus(FocusState.Pointer);
        var point = e.GetCurrentPoint(Surface);
        var shift = KeyDown(VirtualKey.Shift);
        if (point.Properties.IsRightButtonPressed && !(_screen.MouseMode > 0 && !shift))
        {
            if (_hasSelection)
            {
                CopySelection();
            }
            else
            {
                _ = PasteAsync();
            }

            e.Handled = true;
            return;
        }

        if (_screen.MouseMode > 0 && !shift)
        {
            var (col, row) = ViewportCell(point.Position);
            _mouseDown = true;
            Surface.CapturePointer(e.Pointer);
            Send(VtInput.EncodeMouse(point.Properties.IsRightButtonPressed ? 2 : point.Properties.IsMiddleButtonPressed ? 1 : 0, col, row, false, false, _screen.MouseSgr));
            return;
        }

        if (point.Properties.IsLeftButtonPressed)
        {
            _selecting = true;
            _hasSelection = false;
            _selStart = _selEnd = CellAt(point.Position);
            Surface.CapturePointer(e.Pointer);
            Surface.Invalidate();
        }
    }

    private void Surface_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Surface);
        if (_mouseDown && _screen.MouseMode >= 1002)
        {
            var (col, row) = ViewportCell(point.Position);
            Send(VtInput.EncodeMouse(0, col, row, false, true, _screen.MouseSgr));
        }
        else if (_selecting)
        {
            _selEnd = CellAt(point.Position);
            _hasSelection = _selEnd != _selStart;
            Surface.Invalidate();
        }
    }

    private void Surface_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_mouseDown)
        {
            var (col, row) = ViewportCell(e.GetCurrentPoint(Surface).Position);
            Send(VtInput.EncodeMouse(0, col, row, true, false, _screen.MouseSgr));
        }

        (_mouseDown, _selecting) = (false, false);
        Surface.ReleasePointerCaptures();
    }

    private void Surface_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(Surface).Properties.MouseWheelDelta;
        var notches = Math.Max(1, Math.Abs(delta) / 120);
        var up = delta > 0;
        if (_screen.MouseMode > 0 && !KeyDown(VirtualKey.Shift))
        {
            var (col, row) = ViewportCell(e.GetCurrentPoint(Surface).Position);
            for (var i = 0; i < notches; i++)
            {
                Send(VtInput.EncodeMouse(up ? 64 : 65, col, row, false, false, _screen.MouseSgr));
            }
        }
        else if (_screen.AltScreen)
        {
            for (var i = 0; i < notches * 3; i++)
            {
                Send(VtInput.EncodeKey(up ? 38 : 40, false, false, false, _screen.AppCursorKeys)!);
            }
        }
        else
        {
            ScrollBy(up ? notches * 3 : -notches * 3);
        }

        e.Handled = true;
    }

    private void ScrollBy(int lines)
    {
        _scrollOffset = Math.Clamp(_scrollOffset + lines, 0, _screen.ScrollbackCount);
        Surface.Invalidate();
    }

    private void CopySelection()
    {
        if (!_hasSelection)
        {
            return;
        }

        var (from, to) = OrderedSelection();
        var package = new DataPackage();
        package.SetText(_screen.GetText(from.Line, from.Col, to.Line, to.Col));
        Clipboard.SetContent(package);
        _hasSelection = false;
        Surface.Invalidate();
    }

    private async Task PasteAsync()
    {
        var content = Clipboard.GetContent();
        if (content.Contains(StandardDataFormats.Text))
        {
            Send(VtInput.EncodePaste(await content.GetTextAsync(), _screen.BracketedPaste));
        }
    }

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_session is null)
        {
            return;
        }

        var ctrl = KeyDown(VirtualKey.Control);
        var shift = KeyDown(VirtualKey.Shift);
        var alt = KeyDown(VirtualKey.Menu);
        var vk = (int)e.Key;

        if ((ctrl && shift && e.Key == VirtualKey.C) || (ctrl && !shift && e.Key == VirtualKey.C && _hasSelection))
        {
            CopySelection();
            e.Handled = true;
            return;
        }

        if ((ctrl && e.Key == VirtualKey.V) || (shift && e.Key == VirtualKey.Insert))
        {
            _ = PasteAsync();
            e.Handled = true;
            return;
        }

        if (shift && !ctrl && e.Key is VirtualKey.PageUp or VirtualKey.PageDown && !_screen.AltScreen)
        {
            ScrollBy(e.Key == VirtualKey.PageUp ? _screen.Rows - 1 : -(_screen.Rows - 1));
            e.Handled = true;
            return;
        }

        string? sequence = VtInput.EncodeKey(vk, ctrl && !alt, shift, alt, _screen.AppCursorKeys);
        if (sequence is null && alt && !ctrl && vk is >= 65 and <= 90)
        {
            sequence = "\u001b" + (char)(shift ? vk : vk + 32);
        }

        if (sequence is not null)
        {
            _scrollOffset = 0;
            _hasSelection = false;
            Send(sequence);
            Surface.Invalidate();
            e.Handled = true;
        }
    }

    private void OnCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs e)
    {
        if (_session is null)
        {
            return;
        }

        var ch = e.Character;
        var ctrl = KeyDown(VirtualKey.Control);
        var alt = KeyDown(VirtualKey.Menu);
        if (ch < 0x20 || ch == 0x7F || (ctrl != alt) || (ctrl && alt && false))
        {
            return;
        }

        if (char.IsHighSurrogate(ch))
        {
            _pendingHighSurrogate = ch;
            return;
        }

        var text = char.IsLowSurrogate(ch) && _pendingHighSurrogate != 0 ? new string([_pendingHighSurrogate, ch]) : ch.ToString();
        _pendingHighSurrogate = '\0';
        _scrollOffset = 0;
        _hasSelection = false;
        Send(text);
        Surface.Invalidate();
        e.Handled = true;
    }
}
