using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using QuickStash.Interop;

namespace QuickStash.Services;

/// <summary>
/// System tray icon implemented directly on Shell_NotifyIcon (no WinForms dependency).
/// Left click raises <see cref="Activated"/>; right click opens a themed WPF <see cref="ContextMenu"/>.
/// </summary>
internal sealed class TrayIconService : IDisposable
{
    private const uint IconId = 1;
    private const int CallbackMessage = NativeMethods.WM_USER + 0x51;

    private readonly MessageWindow _window;
    private readonly uint _taskbarCreatedMessage;
    private readonly IntPtr _icon;
    private readonly string _tooltip;
    private ContextMenu? _menu;
    private bool _added;

    public event EventHandler? Activated;

    public TrayIconService(MessageWindow window, byte[] icoBytes, string tooltip)
    {
        _window = window;
        _tooltip = tooltip;
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        int size = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSMICON);
        _icon = NativeMethods.CreateIconFromIco(icoBytes, size > 0 ? size : 16);
        window.AddHook(WndProc);
    }

    public ContextMenu? ContextMenu
    {
        get => _menu;
        set
        {
            if (_menu is not null) _menu.Opened -= OnMenuOpened;
            _menu = value;
            if (_menu is not null) _menu.Opened += OnMenuOpened;
        }
    }

    public void Show()
    {
        var data = CreateData(NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP);
        _added = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data);
        if (!_added)
        {
            Log.Error("Shell_NotifyIcon(NIM_ADD) failed");
            return;
        }
        data.uTimeoutOrVersion = NativeMethods.NOTIFYICON_VERSION_4;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_SETVERSION, ref data);
    }

    public void ShowBalloon(string title, string text, bool warning = false)
    {
        if (!_added) return;
        var data = CreateData(NativeMethods.NIF_INFO);
        data.szInfoTitle = Truncate(title, 63);
        data.szInfo = Truncate(text, 255);
        data.dwInfoFlags = warning ? NativeMethods.NIIF_WARNING : NativeMethods.NIIF_INFO;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data);
    }

    private NativeMethods.NOTIFYICONDATAW CreateData(uint flags) => new()
    {
        cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.NOTIFYICONDATAW>(),
        hWnd = _window.Handle,
        uID = IconId,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = Truncate(_tooltip, 127),
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            // NOTIFYICON_VERSION_4: LOWORD(lParam) is the event.
            int evt = (int)(lParam.ToInt64() & 0xFFFF);
            switch (evt)
            {
                case NativeMethods.NIN_SELECT:
                case NativeMethods.NIN_KEYSELECT:
                    Activated?.Invoke(this, EventArgs.Empty);
                    break;
                case NativeMethods.WM_CONTEXTMENU:
                    OpenMenu();
                    break;
            }
            handled = true;
        }
        else if (msg == _taskbarCreatedMessage && _taskbarCreatedMessage != 0)
        {
            // Explorer restarted: our icon is gone, add it again.
            _added = false;
            Show();
        }
        return IntPtr.Zero;
    }

    private void OpenMenu()
    {
        if (_menu is null) return;
        _menu.Placement = PlacementMode.MousePoint;
        _menu.IsOpen = true;
    }

    private void OnMenuOpened(object sender, RoutedEventArgs e)
    {
        // A popup owned by a background process won't close when clicking elsewhere unless it is foreground.
        if (PresentationSource.FromVisual(_menu!) is HwndSource source)
            NativeMethods.SetForegroundWindow(source.Handle);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    public void Dispose()
    {
        if (_added)
        {
            var data = CreateData(0);
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);
            _added = false;
        }
        if (_icon != IntPtr.Zero) NativeMethods.DestroyIcon(_icon);
    }
}
