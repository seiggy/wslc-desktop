using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WslcDesktop.Platform;
using WslcDesktop.Services;
using Windows.ApplicationModel.DataTransfer;

namespace WslcDesktop.Controls;

/// <summary>
/// A real terminal: Windows Terminal's native HwndTerminal (Microsoft.Terminal.Control.dll) hosted as a child window over a
/// XAML placeholder, fed by a Windows pseudoconsole (ConPTY) session. <see cref="StartAsync"/> throws when the terminal can't
/// initialise so the caller can fall back to the simple shell.
/// </summary>
public sealed partial class TerminalView : UserControl
{
    private const int VkC = 0x43, VkV = 0x56, VkControl = 0x11, VkShift = 0x10, VkInsert = 0x2D;

    private readonly object _outputGate = new();
    private readonly List<byte> _outputBuffer = [];
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly Decoder _decoder = new UTF8Encoding(false).GetDecoder();
    private readonly NativeTerminal.SubclassProc _subclass;
    private readonly IntPtr _subclassPtr;
    private ConPtySession? _session;
    private IntPtr _terminal;
    private IntPtr _hwnd;
    private bool _flushQueued;
    private bool _wantVisible;
    private int _pixelWidth, _pixelHeight;
    private short _columns = TerminalProtocol.DefaultColumns;
    private short _rows = TerminalProtocol.DefaultRows;
    private string _fontFamily = TerminalProtocol.Fonts[0];
    private int _fontSize = 14;

    // Native callbacks must stay rooted for the lifetime of the terminal.
    private NativeTerminal.WriteCallback? _write;
    private NativeTerminal.ScrollCallback? _scroll;

    public TerminalView()
    {
        InitializeComponent();
        _subclass = SubclassProc;
        _subclassPtr = Marshal.GetFunctionPointerForDelegate(_subclass);
        ActualThemeChanged += (_, _) => ApplyStyle();
        Loaded += (_, _) => { _wantVisible = Visibility == Visibility.Visible; UpdatePlacement(); };
        Unloaded += (_, _) => UpdatePlacement(hide: true);
        SizeChanged += (_, _) => UpdatePlacement();
        LayoutUpdated += (_, _) => UpdatePlacement();
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) =>
        {
            _wantVisible = Visibility == Visibility.Visible;
            UpdatePlacement();
        });
    }

    public bool IsRunning => _session is not null;

    private bool IsDark => ActualTheme != ElementTheme.Light;

    public async Task StartAsync(string session, string container, string shell, Action<int> onExit)
    {
        Stop();
        EnsureTerminal();
        ResetTerminal();

        var commandLine = TerminalProtocol.BuildCommandLine("wslc", TerminalProtocol.ExecShellArguments(session, container, shell));
        ConPtySession? started = null;
        started = ConPtySession.Start(commandLine, _columns, _rows, QueueOutput, code => _dispatcher.TryEnqueue(() =>
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
        if (_terminal != IntPtr.Zero)
        {
            Send($"\r\n\u001b[33m{text}\u001b[0m\r\n");
        }
    }

    public void FocusTerminal()
    {
        if (_hwnd != IntPtr.Zero)
        {
            NativeTerminal.SetFocus(_hwnd);
        }
    }

    public void SetFont(string family, int size)
    {
        (_fontFamily, _fontSize) = (family, size);
        ApplyStyle();
    }

    private void EnsureTerminal()
    {
        if (_terminal != IntPtr.Zero)
        {
            return;
        }

        try
        {
            NativeTerminal.AvoidBuggyTSFConsoleFlags();
            // The packaged native build predates the dispatcher-callout variant: CreateTerminal takes (parent, out hwnd, out terminal).

            NativeTerminal.CreateTerminal(App.WindowHandle, out _hwnd, out _terminal);
            _write = text => _session?.Write(TerminalProtocol.EncodeInput(text));
            _scroll = (_, _, _) => { };
            _ = NativeTerminal.TerminalRegisterWriteCallback(_terminal, _write);
            NativeTerminal.TerminalRegisterScrollCallback(_terminal, _scroll);
            NativeTerminal.SetWindowSubclass(_hwnd, _subclassPtr, 1, UIntPtr.Zero);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or COMException)
        {
            DestroyTerminal();
            throw new InvalidOperationException("The native Windows Terminal control could not be loaded.", ex);
        }

        ApplyStyle();
        UpdatePlacement();
    }

    private void DestroyTerminal()
    {
        if (_hwnd != IntPtr.Zero)
        {
            NativeTerminal.RemoveWindowSubclass(_hwnd, _subclassPtr, 1);
        }

        if (_terminal != IntPtr.Zero)
        {
            NativeTerminal.DestroyTerminal(_terminal);
        }

        (_terminal, _hwnd) = (IntPtr.Zero, IntPtr.Zero);
    }

    private void ResetTerminal() => Send("\u001bc\u001b]104\u001b\\\u001b[?25h");

    private void Send(string text)
    {
        if (_terminal != IntPtr.Zero)
        {
            NativeTerminal.TerminalSendOutput(_terminal, text);
        }
    }

    private static uint ColorRef(string hex) =>
        Convert.ToUInt32(hex[1..3], 16) | (Convert.ToUInt32(hex[3..5], 16) << 8) | (Convert.ToUInt32(hex[5..7], 16) << 16);

    private void ApplyStyle()
    {
        if (_terminal == IntPtr.Zero)
        {
            return;
        }

        var p = TerminalProtocol.Palette(IsDark);
        string[] order =
        [
            "black", "red", "green", "yellow", "blue", "magenta", "cyan", "white",
            "brightBlack", "brightRed", "brightGreen", "brightYellow", "brightBlue", "brightMagenta", "brightCyan", "brightWhite",
        ];
        var theme = new NativeTerminal.TerminalTheme
        {
            DefaultBackground = ColorRef(p["background"]),
            DefaultForeground = ColorRef(p["foreground"]),
            DefaultSelectionBackground = ColorRef(p["selectionBackground"]),
            CursorStyle = NativeTerminal.CursorBar,
            ColorTable = [.. order.Select(n => ColorRef(p[n]))],
        };

        NativeTerminal.TerminalSetTheme(_terminal, theme, _fontFamily, (short)_fontSize, (int)NativeTerminal.GetDpiForWindow(_hwnd));
        ApplyPixelSize();
    }

    // Re-run the native layout so rows/columns follow a new font or DPI.
    private void ApplyPixelSize()
    {
        if (_terminal != IntPtr.Zero && _pixelWidth > 0 && _pixelHeight > 0)
        {
            NativeTerminal.TerminalTriggerResize(_terminal, _pixelWidth, _pixelHeight, out var size);
            OnGridChanged(size);
        }
    }

    private void OnGridChanged(NativeTerminal.TilSize size)
    {
        if (size.X > 0 && size.Y > 0)
        {
            (_columns, _rows) = (TerminalProtocol.ClampDimension(size.X), TerminalProtocol.ClampDimension(size.Y));
            _session?.Resize(_columns, _rows);
        }
    }

    /// <summary>Positions the native window over the XAML frame (client pixels of the app window), or hides it.</summary>
    private void UpdatePlacement(bool hide = false)
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        if (hide || !_wantVisible || XamlRoot is null || !IsLoaded || ActualWidth < 2 || ActualHeight < 2)
        {
            NativeTerminal.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                NativeTerminal.SWP_HIDEWINDOW | NativeTerminal.SWP_NOMOVE | NativeTerminal.SWP_NOSIZE | NativeTerminal.SWP_NOACTIVATE);
            return;
        }

        var scale = XamlRoot.RasterizationScale;
        var origin = Frame.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
        var inset = Frame.BorderThickness.Left;
        var x = (int)Math.Round((origin.X + inset) * scale);
        var y = (int)Math.Round((origin.Y + inset) * scale);
        var w = Math.Max(1, (int)Math.Round((Frame.ActualWidth - 2 * inset) * scale));
        var h = Math.Max(1, (int)Math.Round((Frame.ActualHeight - 2 * inset) * scale));
        NativeTerminal.SetWindowPos(_hwnd, NativeTerminal.HWND_TOP, x, y, w, h, NativeTerminal.SWP_SHOWWINDOW | NativeTerminal.SWP_NOACTIVATE);
    }

    private IntPtr SubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr refData)
    {
        var terminal = _terminal;
        if (terminal == IntPtr.Zero)
        {
            return NativeTerminal.DefSubclassProc(hwnd, msg, wParam, lParam);
        }

        switch (msg)
        {
            case NativeTerminal.WM_KEYDOWN or NativeTerminal.WM_SYSKEYDOWN or NativeTerminal.WM_KEYUP or NativeTerminal.WM_SYSKEYUP:
            {
                var down = msg is NativeTerminal.WM_KEYDOWN or NativeTerminal.WM_SYSKEYDOWN;
                var vkey = (int)wParam;
                if (down && HandleShortcut(vkey))
                {
                    return IntPtr.Zero;
                }

                var scan = (ushort)(((long)lParam >> 16) & 0xFF);
                var flags = (ushort)(((long)lParam >> 16) & 0xFF00);
                NativeTerminal.TerminalSendKeyEvent(terminal, (ushort)vkey, scan, flags, down);
                return IntPtr.Zero;
            }

            case NativeTerminal.WM_CHAR:
            {
                var scan = (ushort)(((long)lParam >> 16) & 0xFF);
                var flags = (ushort)(((long)lParam >> 16) & 0xFF00);
                NativeTerminal.TerminalSendCharEvent(terminal, (char)wParam, scan, flags);
                return IntPtr.Zero;
            }

            case NativeTerminal.WM_MOUSEACTIVATE:
                NativeTerminal.SetFocus(hwnd);
                return (IntPtr)1; // MA_ACTIVATE

            case NativeTerminal.WM_RBUTTONDOWN:
                // Windows Terminal convention: right click copies an active selection, otherwise pastes.
                if (NativeTerminal.TerminalIsSelectionActive(terminal))
                {
                    CopySelection();
                }
                else
                {
                    Paste();
                }

                return IntPtr.Zero;

            case NativeTerminal.WM_WINDOWPOSCHANGED:
            {
                var pos = Marshal.PtrToStructure<WindowPos>(lParam);
                if ((pos.Flags & NativeTerminal.SWP_NOSIZE) == 0 && pos.Width > 0 && pos.Height > 0)
                {
                    (_pixelWidth, _pixelHeight) = (pos.Width, pos.Height);
                    NativeTerminal.TerminalTriggerResize(terminal, pos.Width, pos.Height, out var size);
                    OnGridChanged(size);
                }

                break;
            }
        }

        return NativeTerminal.DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    // Ctrl+C copies only while text is selected (otherwise it stays SIGINT); Ctrl+V / Ctrl+Shift+V / Shift+Insert paste.
    private bool HandleShortcut(int vkey)
    {
        var ctrl = NativeTerminal.GetKeyState(VkControl) < 0;
        var shift = NativeTerminal.GetKeyState(VkShift) < 0;
        if (ctrl && vkey == VkC && (shift || NativeTerminal.TerminalIsSelectionActive(_terminal)))
        {
            CopySelection();
            return true;
        }

        if ((ctrl && vkey == VkV) || (shift && vkey == VkInsert))
        {
            Paste();
            return true;
        }

        return false;
    }

    public void CopySelection()
    {
        if (_terminal == IntPtr.Zero || !NativeTerminal.TerminalIsSelectionActive(_terminal))
        {
            return;
        }

        var text = NativeTerminal.TerminalGetSelection(_terminal);
        if (!string.IsNullOrEmpty(text))
        {
            var data = new DataPackage();
            data.SetText(text);
            Clipboard.SetContent(data);
        }
    }

    public async void Paste()
    {
        try
        {
            var content = Clipboard.GetContent();
            if (content.Contains(StandardDataFormats.Text))
            {
                var text = await content.GetTextAsync();
                if (!string.IsNullOrEmpty(text))
                {
                    _session?.Write(TerminalProtocol.EncodeInput(text.Replace("\r\n", "\r").Replace('\n', '\r')));
                }
            }
        }
        catch (COMException)
        {
            // Clipboard busy or unavailable.
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public IntPtr Hwnd, InsertAfter;
        public int X, Y, Width, Height;
        public uint Flags;
    }

    private void QueueOutput(byte[] buffer, int count)
    {
        lock (_outputGate)
        {
            for (var i = 0; i < count; i++)
            {
                _outputBuffer.Add(buffer[i]);
            }

            if (_flushQueued)
            {
                return;
            }

            _flushQueued = true;
        }

        _dispatcher.TryEnqueue(FlushOutput);
    }

    private void FlushOutput()
    {
        byte[] data;
        lock (_outputGate)
        {
            data = [.. _outputBuffer];
            _outputBuffer.Clear();
            _flushQueued = false;
        }

        if (data.Length == 0 || _terminal == IntPtr.Zero)
        {
            return;
        }

        var chars = new char[_decoder.GetCharCount(data, 0, data.Length)];
        var n = _decoder.GetChars(data, 0, data.Length, chars, 0);
        if (n > 0)
        {
            NativeTerminal.TerminalSendOutput(_terminal, new string(chars, 0, n));
        }
    }
}
