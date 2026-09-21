using System.Runtime.InteropServices;
using System.Text;

namespace QuickStash.Interop;

/// <summary>
/// Every Win32 P/Invoke used by QuickStash lives here. Nothing else in the app calls native code directly.
/// Grouped by area: messages, hotkeys, tray icon, foreground/process, window styles/position, monitors/DPI, GDI capture.
/// </summary>
internal static class NativeMethods
{
    // ───────────────────────────── Messages ─────────────────────────────

    public const int WM_USER = 0x0400;
    public const int WM_HOTKEY = 0x0312;
    public const int WM_CONTEXTMENU = 0x007B;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_MOUSEACTIVATE = 0x0021;
    public const int MA_NOACTIVATE = 3;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint RegisterWindowMessage(string lpString);

    // ───────────────────────────── Hotkeys ─────────────────────────────

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // ───────────────────────────── Tray icon ─────────────────────────────

    public const uint NIM_ADD = 0x0;
    public const uint NIM_MODIFY = 0x1;
    public const uint NIM_DELETE = 0x2;
    public const uint NIM_SETVERSION = 0x4;
    public const uint NIF_MESSAGE = 0x01;
    public const uint NIF_ICON = 0x02;
    public const uint NIF_TIP = 0x04;
    public const uint NIF_INFO = 0x10;
    public const uint NIF_SHOWTIP = 0x80;
    public const uint NIIF_INFO = 0x1;
    public const uint NIIF_WARNING = 0x2;
    public const uint NOTIFYICON_VERSION_4 = 4;
    public const int NIN_SELECT = WM_USER + 0;
    public const int NIN_KEYSELECT = NIN_SELECT | 0x1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATAW
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATAW lpData);

    public const int SM_CXSMICON = 49;
    public const int SM_CYSMICON = 50;

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CreateIconFromResourceEx(byte[] presbits, uint dwResSize, [MarshalAs(UnmanagedType.Bool)] bool fIcon,
        uint dwVer, int cxDesired, int cyDesired, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>Creates an HICON from an .ico file's bytes, choosing the entry closest to (and not smaller than) <paramref name="size"/>.</summary>
    public static IntPtr CreateIconFromIco(byte[] ico, int size)
    {
        int count = BitConverter.ToUInt16(ico, 4);
        int best = -1, bestSize = int.MaxValue, largest = -1, largestSize = 0;
        for (int i = 0; i < count; i++)
        {
            int e = 6 + 16 * i;
            int s = ico[e] == 0 ? 256 : ico[e];
            if (s >= size && s < bestSize) { best = i; bestSize = s; }
            if (s > largestSize) { largest = i; largestSize = s; }
        }
        if (best < 0) best = largest;
        int entry = 6 + 16 * best;
        int length = (int)BitConverter.ToUInt32(ico, entry + 8);
        int offset = (int)BitConverter.ToUInt32(ico, entry + 12);
        var image = new byte[length];
        Buffer.BlockCopy(ico, offset, image, 0, length);
        return CreateIconFromResourceEx(image, (uint)length, true, 0x00030000, size, size, 0);
    }

    // ───────────────────────── Foreground window / process ─────────────────────────

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [DllImport("user32.dll")]
    public static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    public static string GetWindowClassName(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        return GetClassName(hWnd, sb, sb.Capacity) > 0 ? sb.ToString() : string.Empty;
    }

    /// <summary>
    /// Brings <paramref name="hWnd"/> to the foreground. Usually a plain SetForegroundWindow succeeds because the
    /// hotkey press was our input; if Windows' foreground lock refuses, briefly attach to the foreground thread's input queue.
    /// </summary>
    public static void ForceForeground(IntPtr hWnd)
    {
        if (GetForegroundWindow() == hWnd) return;
        if (SetForegroundWindow(hWnd) && GetForegroundWindow() == hWnd) return;

        uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        uint ourThread = GetCurrentThreadId();
        if (fgThread != 0 && fgThread != ourThread && AttachThreadInput(ourThread, fgThread, true))
        {
            try
            {
                BringWindowToTop(hWnd);
                SetForegroundWindow(hWnd);
            }
            finally
            {
                AttachThreadInput(ourThread, fgThread, false);
            }
        }
    }

    // ───────────────────────── Window styles / position ─────────────────────────

    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TRANSPARENT = 0x00000020;
    public const long WS_EX_TOOLWINDOW = 0x00000080;
    public const long WS_EX_APPWINDOW = 0x00040000;
    public const long WS_EX_LAYERED = 0x00080000;
    public const long WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_POPUP = unchecked((int)0x80000000);

    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    public static long GetExStyle(IntPtr hWnd) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, GWL_EXSTYLE).ToInt64() : GetWindowLong32(hWnd, GWL_EXSTYLE);

    public static void SetExStyle(IntPtr hWnd, long style)
    {
        if (IntPtr.Size == 8) SetWindowLongPtr64(hWnd, GWL_EXSTYLE, new IntPtr(style));
        else SetWindowLong32(hWnd, GWL_EXSTYLE, (int)style);
    }

    /// <summary>Adds and removes extended style bits in one go.</summary>
    public static void UpdateExStyle(IntPtr hWnd, long add, long remove = 0)
    {
        long current = GetExStyle(hWnd);
        long updated = (current | add) & ~remove;
        if (updated != current) SetExStyle(hWnd, updated);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    /// <summary>Re-asserts topmost Z-order without moving, resizing or activating.</summary>
    public static void BringToTopmost(IntPtr hWnd) =>
        SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X, Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    /// <summary>Hides a window from screenshots/recordings (Windows 10 2004+). Used so pinned notes don't end up in captures.</summary>
    public const uint WDA_NONE = 0x0;
    public const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowDisplayAffinity(IntPtr hWnd, out uint pdwAffinity);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    /// <summary>All visible top-level windows of this process, including popups such as tooltips and menus.</summary>
    public static List<IntPtr> GetOwnVisibleWindows()
    {
        var result = new List<IntPtr>();
        uint self = (uint)Environment.ProcessId;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == self && IsWindowVisible(hwnd)) result.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return result;
    }

    /// <summary>Waits until the desktop compositor has presented the next frame (so affinity/visibility changes are on screen).</summary>
    [DllImport("dwmapi.dll")]
    public static extern int DwmFlush();

    // ───────────────────────────── Foreground change events ─────────────────────────────

    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    public delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime);

    /// <summary>Caller must keep <paramref name="proc"/> alive (e.g. in a field) for as long as the hook exists.</summary>
    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc proc,
        uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    // ───────────────────────────── Monitors / DPI ─────────────────────────────

    public const uint MONITOR_DEFAULTTONULL = 0;

    /// <summary>True when the point (physical pixels) lies on any monitor.</summary>
    public static bool IsOnAnyMonitor(int x, int y) =>
        MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONULL) != IntPtr.Zero;

    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hWnd);

    /// <summary>
    /// Work area (physical pixels) and DPI scale of the monitor showing <paramref name="hwnd"/>.
    /// Falls back to the cursor's monitor when the window is gone, then to the primary monitor.
    /// </summary>
    public static (RECT WorkArea, double Scale) GetMonitorForWindow(IntPtr hwnd)
    {
        IntPtr monitor = hwnd != IntPtr.Zero && IsWindow(hwnd) ? MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST) : IntPtr.Zero;
        if (monitor == IntPtr.Zero && GetCursorPos(out var cursor))
            monitor = MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST);

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info) || info.rcWork.Width <= 0)
        {
            monitor = MonitorFromPoint(default, 1 /* MONITOR_DEFAULTTOPRIMARY */);
            GetMonitorInfo(monitor, ref info);
        }
        double scale = GetDpiForMonitor(monitor, 0 /* MDT_EFFECTIVE_DPI */, out uint dpiX, out _) == 0 ? dpiX / 96.0 : 1.0;
        return (info.rcWork, scale);
    }

    // ───────────────────────────── Memory ─────────────────────────────

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr min, IntPtr max);

    /// <summary>
    /// Returns idle pages to Windows (they stay in the standby list and come back cheaply on the next use).
    /// Called when QuickStash goes back to sitting in the tray.
    /// </summary>
    public static void TrimWorkingSet() => SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));

    // ───────────────────────────── Screen capture ─────────────────────────────

    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    /// <summary>Visible bounds of a window (without the invisible resize borders Windows 10+ adds), in physical pixels.</summary>
    public static bool TryGetVisibleBounds(IntPtr hwnd, out RECT rect)
    {
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf<RECT>()) == 0 && rect.Width > 0 && rect.Height > 0)
            return true;
        return GetWindowRect(hwnd, out rect) && rect.Width > 0 && rect.Height > 0;
    }

    public const int SRCCOPY = 0x00CC0020;
    public const int CAPTUREBLT = 0x40000000;

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BitBlt(IntPtr hdc, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, int rop);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr ho);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteDC(IntPtr hdc);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint cLines, byte[] lpvBits, ref BITMAPINFOHEADER lpbmi, uint usage);

    /// <summary>
    /// Copies a screen rectangle (physical pixels) and returns its pixels as top-down 32bpp BGRX.
    /// Reading from the screen DC (rather than PrintWindow) is what works for DirectX games running borderless-windowed.
    /// </summary>
    public static byte[]? CaptureScreenPixels(RECT r)
    {
        if (r.Width <= 0 || r.Height <= 0) return null;

        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr bitmap = CreateCompatibleBitmap(screenDc, r.Width, r.Height);
        IntPtr old = SelectObject(memDc, bitmap);
        try
        {
            if (!BitBlt(memDc, 0, 0, r.Width, r.Height, screenDc, r.Left, r.Top, SRCCOPY | CAPTUREBLT))
                return null;
            SelectObject(memDc, old);
            old = IntPtr.Zero;

            var header = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = r.Width,
                biHeight = -r.Height, // negative = top-down rows
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0, // BI_RGB
            };
            var pixels = new byte[r.Width * r.Height * 4];
            return GetDIBits(memDc, bitmap, 0, (uint)r.Height, pixels, ref header, 0 /* DIB_RGB_COLORS */) == r.Height ? pixels : null;
        }
        finally
        {
            if (old != IntPtr.Zero) SelectObject(memDc, old);
            DeleteObject(bitmap);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
}
