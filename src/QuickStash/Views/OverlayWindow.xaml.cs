using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using QuickStash.Interop;
using QuickStash.ViewModels;

namespace QuickStash.Views;

/// <summary>
/// The overlay. Created once at startup and kept alive; opening is just positioning + Show(), which keeps it well under 200 ms.
/// Code-behind only handles window mechanics (styles, placement, focus, Esc); the content is a <see cref="NotesView"/>.
/// </summary>
public partial class OverlayWindow : Window
{
    private IntPtr _hwnd;
    private IntPtr _targetWindow;

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        DpiChanged += OnDpiChanged;
        KeyDown += OnWindowKeyDown; // bubbling: inline editors get to handle Esc first
        Notes.CloseRequested += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when the overlay wants to close (Esc, close button, or focus lost).</summary>
    public event EventHandler? CloseRequested;

    /// <summary>When true, losing focus does not close the overlay.</summary>
    public bool SuppressAutoHide { get; set; }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        // Tool window: never appears in Alt+Tab or the taskbar.
        NativeMethods.UpdateExStyle(_hwnd, NativeMethods.WS_EX_TOOLWINDOW, NativeMethods.WS_EX_APPWINDOW);
    }

    /// <summary>Creates the HWND and renders once off-screen so the first real open is as fast as later ones.</summary>
    public void WarmUp()
    {
        ShowActivated = false;
        Left = -20000;
        Top = -20000;
        Show();
        Hide();
        ShowActivated = true;
    }

    public void ShowCentered(IntPtr targetWindow)
    {
        _targetWindow = targetWindow;
        CenterOn(targetWindow);
        Show();
        Activate();
        NativeMethods.ForceForeground(_hwnd);
        Notes.ApplyPendingFocus();
    }

    /// <summary>Centers on the work area of the monitor showing <paramref name="targetWindow"/> (physical pixels, DPI-safe).</summary>
    private void CenterOn(IntPtr targetWindow)
    {
        if (_hwnd == IntPtr.Zero) return;
        var (work, scale) = NativeMethods.GetMonitorForWindow(targetWindow);
        int width = (int)Math.Round(Width * scale);
        int height = (int)Math.Round(Height * scale);
        int x = work.Left + (work.Width - width) / 2;
        int y = work.Top + (work.Height - height) / 2;
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    // Moving onto a monitor with a different scale resizes the window; re-center it with its new size.
    private void OnDpiChanged(object sender, DpiChangedEventArgs e)
    {
        // WPF also raises this with an unchanged DPI (e.g. on focus changes); only a real scale change needs re-centering.
        if (e.OldDpi.PixelsPerInchX.Equals(e.NewDpi.PixelsPerInchX) || !IsVisible) return;
        Dispatcher.BeginInvoke(() => CenterOn(_targetWindow));
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (IsVisible && !SuppressAutoHide) CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        if ((DataContext as OverlayViewModel)?.HandleEscape() != true) CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
