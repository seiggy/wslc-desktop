using System.Runtime.InteropServices;
using Microsoft.Win32;
using WslcDesktop.Services;
using WslcDesktop.Lifecycle;

namespace WslcDesktop.Platform;

/// <summary>
/// Notification-area icon via Shell_NotifyIcon. It owns a hidden top-level (not message-only) window, because
/// only top-level windows receive the broadcast TaskbarCreated and WM_SETTINGCHANGE (theme) messages.
/// Must be created and disposed on the UI thread, which pumps its messages.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const string ClassName = "WslcDesktop.TrayWindow";
    private const uint WmTray = 0x8000 + 1;
    private const uint NimAdd = 0, NimModify = 1, NimDelete = 2, NimSetVersion = 4;
    private const uint NifMessage = 1, NifIcon = 2, NifTip = 4;
    private const uint WmLButtonUp = 0x202, WmContextMenu = 0x7B, NinKeySelect = 0x401;
    private const uint WmSettingChange = 0x1A, WmDisplayChange = 0x7E, WmDpiChanged = 0x2E0;
    private const int IdOpen = 1, IdExit = 2;

    private readonly WndProc _proc;
    private readonly uint _taskbarCreated;
    private readonly string _assetDir;
    private nint _hwnd;
    private nint _icon;
    private bool _added;
    private string _tooltip = "WSLc Desktop Manager";

    public TrayIcon(string assetDir)
    {
        _assetDir = assetDir;
        _proc = WindowProc;
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        var hInstance = GetModuleHandle(null);
        var cls = new WndClassEx
        {
            cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
            hInstance = hInstance,
            lpszClassName = ClassName,
        };
        RegisterClassEx(ref cls);
        _hwnd = CreateWindowEx(0x80 /* WS_EX_TOOLWINDOW */, ClassName, "WslcDesktopTray", 0x80000000 /* WS_POPUP */, 0, 0, 0, 0, 0, 0, hInstance, 0);
        TryEnableDarkMenus();
    }

    public event Action? OpenRequested;

    public event Action? ExitRequested;

    // Supplies the status line shown in the context menu when it opens.
    public Func<string>? StatusProvider { get; set; }

    public bool IsAdded => _added;

    public void Show()
    {
        if (_hwnd == 0)
        {
            return;
        }

        ReloadIcon();
        if (!_added)
        {
            _added = Notify(NimAdd);
            if (_added)
            {
                var data = Build();
                data.uVersion = 4;
                Shell_NotifyIcon(NimSetVersion, ref data);
            }
        }
        else
        {
            Notify(NimModify);
        }
    }

    public void Hide()
    {
        if (_added)
        {
            var data = Build();
            Shell_NotifyIcon(NimDelete, ref data);
            _added = false;
        }
    }

    public void SetTooltip(string text)
    {
        _tooltip = text;
        if (_added)
        {
            Notify(NimModify);
        }
    }

    public void Dispose()
    {
        Hide();
        if (_hwnd != 0)
        {
            DestroyWindow(_hwnd);
            _hwnd = 0;
            UnregisterClass(ClassName, GetModuleHandle(null));
        }

        if (_icon != 0)
        {
            DestroyIcon(_icon);
            _icon = 0;
        }
    }

    private static bool TaskbarIsLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int v && v != 0;
    }

    private static int SmallIconPixels()
    {
        uint dpi = 96;
        var monitor = MonitorFromPoint(default, 1 /* MONITOR_DEFAULTTOPRIMARY */);
        if (GetDpiForMonitor(monitor, 0, out var dx, out _) == 0 && dx > 0)
        {
            dpi = dx;
        }

        return GetSystemMetricsForDpi(49 /* SM_CXSMICON */, dpi);
    }

    private void ReloadIcon()
    {
        var pixels = SmallIconPixels();
        var light = TaskbarIsLight();
        var next = LoadPng(Path.Combine(_assetDir, TrayPolicy.IconFileName(light, pixels)), pixels);
        var fallback = Path.Combine(_assetDir, light ? "tray-light.ico" : "tray-dark.ico");
        if (next == 0 && File.Exists(fallback))
        {
            next = LoadImage(0, fallback, 1 /* IMAGE_ICON */, pixels, pixels, 0x10 /* LR_LOADFROMFILE */);
        }

        if (next == 0)
        {
            return;
        }

        var old = _icon;
        _icon = next;
        if (_added)
        {
            Notify(NimModify);
        }

        if (old != 0)
        {
            DestroyIcon(old);
        }
    }

    private static nint LoadPng(string path, int pixels)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            return CreateIconFromResourceEx(bytes, (uint)bytes.Length, true, 0x00030000, pixels, pixels, 0);
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private NotifyIconData Build() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
        hWnd = _hwnd,
        uID = 1,
        uFlags = NifMessage | NifIcon | NifTip,
        uCallbackMessage = WmTray,
        hIcon = _icon,
        szTip = _tooltip.Length > 127 ? _tooltip[..127] : _tooltip,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private bool Notify(uint message)
    {
        var data = Build();
        return Shell_NotifyIcon(message, ref data);
    }

    private nint WindowProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WmTray)
        {
            switch ((uint)((long)lParam & 0xFFFF))
            {
                case WmLButtonUp:
                case NinKeySelect:
                    OpenRequested?.Invoke();
                    break;
                case WmContextMenu:
                    ShowMenu((short)((long)wParam & 0xFFFF), (short)(((long)wParam >> 16) & 0xFFFF));
                    break;
            }

            return 0;
        }

        if (_taskbarCreated != 0 && msg == _taskbarCreated)
        {
            // Explorer restarted; every notification icon has to be added again.
            _added = false;
            Show();
            return 0;
        }

        if ((msg == WmSettingChange && lParam != 0 && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet")
            || msg == WmDisplayChange || msg == WmDpiChanged)
        {
            ReloadIcon();
            return 0;
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void ShowMenu(int x, int y)
    {
        var menu = CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            AppendMenu(menu, 0, IdOpen, "Open WSLc Desktop Manager");
            AppendMenu(menu, 0x3 /* MF_GRAYED */, 0, StatusProvider?.Invoke() ?? "Status unavailable");
            AppendMenu(menu, 0x800 /* MF_SEPARATOR */, 0, null);
            AppendMenu(menu, 0, IdExit, "Exit");
            SetMenuDefaultItem(menu, IdOpen, 0);

            // The owner must be foreground or the menu won't dismiss when the user clicks elsewhere.
            SetForegroundWindow(_hwnd);
            var cmd = TrackPopupMenuEx(menu, 0x100 /* RETURNCMD */ | 0x20 /* BOTTOMALIGN */ | 0x8 /* RIGHTBUTTON */, x, y, _hwnd, 0);
            PostMessage(_hwnd, 0 /* WM_NULL */, 0, 0);
            if (cmd == IdOpen)
            {
                OpenRequested?.Invoke();
            }
            else if (cmd == IdExit)
            {
                ExitRequested?.Invoke();
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    // Undocumented but long-stable uxtheme ordinals (SetPreferredAppMode, FlushMenuThemes); menus follow the system theme.
    private static void TryEnableDarkMenus()
    {
        try
        {
            var ux = LoadLibrary("uxtheme.dll");
            var setMode = GetProcAddress(ux, 135);
            var flush = GetProcAddress(ux, 136);
            if (setMode != 0)
            {
                Marshal.GetDelegateForFunctionPointer<SetPreferredAppMode>(setMode)(1 /* AllowDark */);
            }

            if (flush != 0)
            {
                Marshal.GetDelegateForFunctionPointer<Flush>(flush)();
            }
        }
        catch (Exception)
        {
            // Cosmetic only.
        }
    }

    private delegate int SetPreferredAppMode(int mode);

    private delegate void Flush();

    private delegate nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WndClassEx cls);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClass(string name, nint hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowEx(uint exStyle, string cls, string title, uint style, int x, int y, int w, int h, nint parent, nint menu, nint hInstance, nint param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(nint menu, uint flags, nuint id, string? text);

    [DllImport("user32.dll")]
    private static extern bool SetMenuDefaultItem(nint menu, uint item, uint byPosition);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint tpm);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(nint hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint icon);

    [DllImport("user32.dll")]
    private static extern nint CreateIconFromResourceEx(byte[] bits, uint size, bool icon, uint version, int cx, int cy, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadImage(nint hInstance, string name, uint type, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(Point pt, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadLibrary(string name);

    [DllImport("kernel32.dll")]
    private static extern nint GetProcAddress(nint module, nint ordinal);
}
