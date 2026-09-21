using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using QuickStash.Interop;
using QuickStash.ViewModels;

namespace QuickStash.Views;

/// <summary>
/// A small always-on-top note over the game. Never takes focus (WS_EX_NOACTIVATE). While the overlay is closed it is
/// click-through (WS_EX_TRANSPARENT), so the game keeps receiving every click; while the overlay is open it can be dragged.
/// Positions are physical pixels of the window's top-left corner.
/// </summary>
public partial class PinnedNoteWindow : Window
{
    private IntPtr _hwnd;
    private bool _interactive;
    private bool _dragging;
    private NativeMethods.POINT _dragStartCursor;
    private NativeMethods.RECT _dragStartWindow;

    public PinnedNoteWindow(PinnedNoteViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += OnSourceInitialized;
        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        LostMouseCapture += (_, _) => EndDrag();
    }

    public PinnedNoteViewModel ViewModel => (PinnedNoteViewModel)DataContext;

    /// <summary>Raised after the user finished dragging, with the new top-left (physical pixels).</summary>
    public event EventHandler<(int X, int Y)>? Moved;

    public bool ExcludeFromCapture { get; set; } = true;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        ApplyStyles();
        ApplyCaptureExclusion();
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Clicking (to drag) must never activate this window: that would deactivate, and close, the overlay.
        if (msg == NativeMethods.WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(NativeMethods.MA_NOACTIVATE);
        }
        return IntPtr.Zero;
    }

    public void SetInteractive(bool interactive)
    {
        _interactive = interactive;
        ViewModel.IsInteractive = interactive;
        Frame.BorderBrush = interactive ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("BorderBrush");
        Cursor = interactive ? Cursors.SizeAll : null;
        ApplyStyles();
        BringToTop();
    }

    private void ApplyStyles()
    {
        if (_hwnd == IntPtr.Zero) return;
        long always = NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_LAYERED;
        if (_interactive) NativeMethods.UpdateExStyle(_hwnd, always, NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_APPWINDOW);
        else NativeMethods.UpdateExStyle(_hwnd, always | NativeMethods.WS_EX_TRANSPARENT, NativeMethods.WS_EX_APPWINDOW);
    }

    /// <summary>Keeps pinned notes out of the auto-screenshot (and out of recordings) when enabled.</summary>
    public void ApplyCaptureExclusion()
    {
        if (_hwnd == IntPtr.Zero) return;
        NativeMethods.SetWindowDisplayAffinity(_hwnd, ExcludeFromCapture ? NativeMethods.WDA_EXCLUDEFROMCAPTURE : NativeMethods.WDA_NONE);
    }

    public void BringToTop()
    {
        if (_hwnd != IntPtr.Zero) NativeMethods.BringToTopmost(_hwnd);
    }

    public void MoveTo(int x, int y)
    {
        if (_hwnd == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, x, y, 0, 0, NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    public (int X, int Y, int Width, int Height) GetBounds()
    {
        NativeMethods.GetWindowRect(_hwnd, out var r);
        return (r.Left, r.Top, r.Width, r.Height);
    }

    // ───────── Dragging (manual, so it works without ever activating the window) ─────────

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_interactive || e.OriginalSource is DependencyObject source && IsInsideButton(source)) return;
        NativeMethods.GetCursorPos(out _dragStartCursor);
        NativeMethods.GetWindowRect(_hwnd, out _dragStartWindow);
        _dragging = CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        NativeMethods.GetCursorPos(out var cursor);
        MoveTo(_dragStartWindow.Left + cursor.X - _dragStartCursor.X, _dragStartWindow.Top + cursor.Y - _dragStartCursor.Y);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        ReleaseMouseCapture(); // → EndDrag
        e.Handled = true;
    }

    private void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        var (x, y, _, _) = GetBounds();
        if (x != _dragStartWindow.Left || y != _dragStartWindow.Top) Moved?.Invoke(this, (x, y));
    }

    private static bool IsInsideButton(DependencyObject element)
    {
        for (var current = element; current is not null;
             current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
        {
            if (current is System.Windows.Controls.Primitives.ButtonBase) return true;
            if (current is PinnedNoteWindow) return false;
        }
        return false;
    }
}
