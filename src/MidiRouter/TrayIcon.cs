using System.Runtime.InteropServices;

namespace MidiRouter;

/// <summary>
/// The icon in the notification area. Left click raises <see cref="Open"/>; right click shows the
/// menu. Lives on the UI thread: WinUI's message loop pumps the hidden window that owns the icon,
/// so every callback runs on the UI thread. The icon comes back by itself if Explorer restarts.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const uint IconId = 1;
    private const uint WM_APP = 0x8000;
    private const uint WM_TRAY = WM_APP + 1;
    private const uint WM_NULL = 0x0000;
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint WM_USER = 0x0400;
    private const uint NIN_SELECT = WM_USER + 0;
    private const uint NIN_KEYSELECT = WM_USER + 1;

    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 0x01, NIF_ICON = 0x02, NIF_TIP = 0x04, NIF_INFO = 0x10, NIF_SHOWTIP = 0x80;
    private const uint NIIF_USER = 0x04, NIIF_LARGE_ICON = 0x20;
    private const uint NOTIFYICON_VERSION_4 = 4;

    private const uint MF_STRING = 0x0000, MF_SEPARATOR = 0x0800;
    private const uint TPM_RIGHTBUTTON = 0x0002, TPM_BOTTOMALIGN = 0x0020, TPM_RETURNCMD = 0x0100;
    private const uint IMAGE_ICON = 1, LR_LOADFROMFILE = 0x10;
    private const int SM_CXSMICON = 49, SM_CYSMICON = 50;

    private readonly string _iconPath;
    private readonly string _tooltip;
    private readonly List<(string Text, Action Run)> _menu;
    private readonly WndProc _wndProc; // Held so the GC never collects the callback.
    private readonly uint _taskbarCreated;
    private readonly string _className = "MidiRouter.Tray." + Environment.ProcessId;
    private nint _hwnd;
    private nint _icon;
    private bool _added;

    /// <param name="menu">Right-click menu, top to bottom; null text is a separator. The first item is the default (bold).</param>
    public TrayIcon(string tooltip, string iconPath, (string? Text, Action? Run)[] menu)
    {
        _tooltip = tooltip.Length > 127 ? tooltip[..127] : tooltip;
        _iconPath = iconPath;
        _menu = menu.Select(m => (m.Text ?? "", m.Run ?? (() => { }))).ToList();
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        _wndProc = OnMessage;

        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = GetModuleHandleW(null),
            lpszClassName = _className,
        };
        RegisterClassExW(ref wc);
        // A plain hidden window, not message-only, so Explorer's TaskbarCreated broadcast reaches it.
        _hwnd = CreateWindowExW(0, _className, "MIDI Router tray", 0, 0, 0, 0, 0, 0, 0, wc.hInstance, 0);
        DarkMenus.Apply(_hwnd);
        Add();
    }

    /// <summary>Left click, or Enter/Space on the icon.</summary>
    public event Action? Open;

    /// <summary>Shows a one-off notification from the icon.</summary>
    public void ShowBalloon(string title, string text)
    {
        if (!_added) return;
        var data = NewData(NIF_INFO);
        data.szInfoTitle = title;
        data.szInfo = text;
        data.dwInfoFlags = NIIF_USER | NIIF_LARGE_ICON;
        data.hBalloonIcon = _icon;
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    public void Dispose()
    {
        Remove();
        if (_hwnd != 0)
        {
            DestroyWindow(_hwnd);
            _hwnd = 0;
        }
        UnregisterClassW(_className, GetModuleHandleW(null));
    }

    private void Add()
    {
        var size = GetSystemMetrics(SM_CXSMICON);
        _icon = LoadImageW(0, _iconPath, IMAGE_ICON, size, GetSystemMetrics(SM_CYSMICON), LR_LOADFROMFILE);
        var data = NewData(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        _added = Shell_NotifyIconW(NIM_ADD, ref data);
        if (!_added) return;
        data.uTimeoutOrVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIconW(NIM_SETVERSION, ref data);
    }

    private void Remove()
    {
        if (_added)
        {
            var data = NewData(0);
            Shell_NotifyIconW(NIM_DELETE, ref data);
            _added = false;
        }
        if (_icon != 0)
        {
            DestroyIcon(_icon);
            _icon = 0;
        }
    }

    private NOTIFYICONDATAW NewData(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _hwnd,
        uID = IconId,
        uFlags = flags,
        uCallbackMessage = WM_TRAY,
        hIcon = _icon,
        szTip = _tooltip,
        szInfo = "",
        szInfoTitle = "",
    };

    private nint OnMessage(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            if (msg == _taskbarCreated && _taskbarCreated != 0)
            {
                // Explorer restarted and forgot every icon.
                Remove();
                Add();
                return 0;
            }
            if (msg == WM_TRAY)
            {
                // Version 4: the event is in the low word of lParam, the anchor point in wParam.
                var evt = (uint)(lParam & 0xFFFF);
                if (evt is NIN_SELECT or NIN_KEYSELECT) Open?.Invoke();
                else if (evt == WM_CONTEXTMENU) ShowMenu((short)(wParam & 0xFFFF), (short)((wParam >> 16) & 0xFFFF));
                return 0;
            }
        }
        catch (Exception ex)
        {
            // An exception escaping a window procedure would take the whole app down.
            Log.Info($"Tray error: {ex.Message}");
            return 0;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void ShowMenu(int x, int y)
    {
        DarkMenus.Apply(_hwnd);
        var menu = CreatePopupMenu();
        if (menu == 0) return;
        try
        {
            for (var i = 0; i < _menu.Count; i++)
            {
                if (_menu[i].Text.Length == 0) AppendMenuW(menu, MF_SEPARATOR, 0, null);
                else AppendMenuW(menu, MF_STRING, (nuint)(i + 1), _menu[i].Text);
            }
            SetMenuDefaultItem(menu, 1, 0); // The first item, shown bold: what a left click does.
            // Without this the menu does not close when the user clicks somewhere else.
            SetForegroundWindow(_hwnd);
            var chosen = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_BOTTOMALIGN | TPM_RETURNCMD, x, y, _hwnd, 0);
            PostMessageW(_hwnd, WM_NULL, 0, 0);
            if (chosen > 0 && chosen <= _menu.Count) _menu[chosen - 1].Run();
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    /// <summary>
    /// Win32 popup menus are drawn light unless the process asks for dark. This follows the Windows
    /// "system" mode (the one the taskbar uses), so the menu matches the other tray menus.
    /// Uses uxtheme functions exported by ordinal only (Windows 10 1903+); if missing, menus stay light.
    /// </summary>
    private static class DarkMenus
    {
        private const int OrdinalAllowDarkModeForWindow = 133;
        private const int OrdinalSetPreferredAppMode = 135;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int OrdinalFlushMenuThemes = 136;
        private const int ForceDark = 2, ForceLight = 3;

        private delegate int SetPreferredAppModeFn(int mode);
        private delegate bool AllowDarkModeForWindowFn(nint hwnd, bool allow);
        private delegate void FlushMenuThemesFn();

        private static readonly SetPreferredAppModeFn? SetMode;
        private static readonly FlushMenuThemesFn? Flush;
        private static readonly AllowDarkModeForWindowFn? AllowForWindow;

        static DarkMenus()
        {
            var uxtheme = LoadLibraryW("uxtheme.dll");
            if (uxtheme == 0) return;
            var set = GetProcAddress(uxtheme, OrdinalSetPreferredAppMode);
            var flush = GetProcAddress(uxtheme, OrdinalFlushMenuThemes);
            if (set == 0 || flush == 0) return;
            var allow = GetProcAddress(uxtheme, OrdinalAllowDarkModeForWindow);
            if (allow != 0) AllowForWindow = Marshal.GetDelegateForFunctionPointer<AllowDarkModeForWindowFn>(allow);
            SetMode = Marshal.GetDelegateForFunctionPointer<SetPreferredAppModeFn>(set);
            Flush = Marshal.GetDelegateForFunctionPointer<FlushMenuThemesFn>(flush);
        }

        /// <summary>
        /// Checked on every open, so a switch of the Windows mode is picked up. The menu's border
        /// and shadow follow its owner window, so that is marked dark too.
        /// </summary>
        public static void Apply(nint owner)
        {
            if (SetMode is null || Flush is null) return;
            var dark = SystemIsDark();
            SetMode(dark ? ForceDark : ForceLight);
            AllowForWindow?.Invoke(owner, dark);
            var value = dark ? 1 : 0;
            DwmSetWindowAttribute(owner, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
            Flush();
        }

        private static bool SystemIsDark()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("SystemUsesLightTheme") is int light && light == 0;
            }
            catch (Exception)
            {
                return true;
            }
        }
    }

    // ---- Win32 ----------------------------------------------------------------------------

    private delegate nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
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
    private struct NOTIFYICONDATAW
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
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClassW(string className, nint instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadImageW(nint instance, string name, uint type, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint icon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(nint menu, uint flags, nuint id, string? text);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint tpm);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadLibraryW(string name);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool SetMenuDefaultItem(nint menu, uint item, uint byPosition);

    [DllImport("kernel32.dll")]
    private static extern nint GetProcAddress(nint module, nint ordinal);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(nint hwnd, uint msg, nint wParam, nint lParam);
}
