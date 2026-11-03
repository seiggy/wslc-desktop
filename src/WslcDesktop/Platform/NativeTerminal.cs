using System.Runtime.InteropServices;

namespace WslcDesktop.Platform;

/// <summary>P/Invoke surface of Windows Terminal's native <c>Microsoft.Terminal.Control.dll</c> (the HwndTerminal used by Microsoft.Terminal.Wpf).</summary>
internal static partial class NativeTerminal
{
    private const string Dll = "Microsoft.Terminal.Control.dll";

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void ScrollCallback(int viewTop, int viewHeight, int bufferSize);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void WriteCallback([MarshalAs(UnmanagedType.LPWStr)] string data);

    [StructLayout(LayoutKind.Sequential)]
    public struct TerminalTheme
    {
        public uint DefaultBackground;
        public uint DefaultForeground;
        public uint DefaultSelectionBackground;
        public uint CursorStyle;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public uint[] ColorTable;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TilSize
    {
        public int X;
        public int Y;
    }

    public const uint CursorBar = 1;

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall, PreserveSig = false)]
    public static extern void AvoidBuggyTSFConsoleFlags();

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall, PreserveSig = false)]
    public static extern void CreateTerminal(IntPtr parent, out IntPtr hwnd, out IntPtr terminal);

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
    public static extern void DestroyTerminal(IntPtr terminal);

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall, PreserveSig = false)]
    public static extern void TerminalSendOutput(IntPtr terminal, [MarshalAs(UnmanagedType.LPWStr)] string data);

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall, PreserveSig = false)]
    public static extern void TerminalTriggerResize(IntPtr terminal, int width, int height, out TilSize dimensions);

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall, PreserveSig = false)]
    public static extern void TerminalDpiChanged(IntPtr terminal, int newDpi);

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall, PreserveSig = true)]
    public static extern int TerminalRegisterScrollCallback(IntPtr terminal, ScrollCallback callback);

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall, PreserveSig = true)]
    public static extern int TerminalRegisterWriteCallback(IntPtr terminal, WriteCallback callback);

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall, PreserveSig = false)]
    public static extern void TerminalUserScroll(IntPtr terminal, int viewTop);

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall, PreserveSig = false)]
    [return: MarshalAs(UnmanagedType.LPWStr)]
    public static extern string TerminalGetSelection(IntPtr terminal);

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool TerminalIsSelectionActive(IntPtr terminal);

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall, PreserveSig = false)]
    public static extern void TerminalSendKeyEvent(IntPtr terminal, ushort vkey, ushort scanCode, ushort flags, [MarshalAs(UnmanagedType.U1)] bool keyDown);

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall, PreserveSig = false)]
    public static extern void TerminalSendCharEvent(IntPtr terminal, char ch, ushort scanCode, ushort flags);

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall, PreserveSig = false)]
    public static extern void TerminalSetTheme(IntPtr terminal, TerminalTheme theme, [MarshalAs(UnmanagedType.LPWStr)] string fontFamily, short fontSize, int newDpi);

    // --- user32 / comctl32 helpers ---

    public const uint WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_CHAR = 0x0102, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    public const uint WM_MOUSEACTIVATE = 0x0021, WM_SETFOCUS = 0x0007, WM_WINDOWPOSCHANGED = 0x0047, WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204;
    public const uint WM_NCDESTROY = 0x0082;
    public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040, SWP_HIDEWINDOW = 0x0080;
    public static readonly IntPtr HWND_TOP = IntPtr.Zero;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate IntPtr SubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr refData);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowSubclass(IntPtr hwnd, IntPtr proc, UIntPtr id, UIntPtr refData);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RemoveWindowSubclass(IntPtr hwnd, IntPtr proc, UIntPtr id);

    [LibraryImport("comctl32.dll")]
    public static partial IntPtr DefSubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    public static partial IntPtr SetFocus(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    public static partial short GetKeyState(int vkey);
}
