using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QuickStash.Interop;

namespace QuickStash.Views;

/// <summary>
/// A small "Saved to TLD" confirmation in the corner of the game's monitor. It never takes focus, lets clicks through,
/// and is excluded from screen capture, so it can't interrupt the game or show up in the next screenshot.
/// </summary>
public partial class ToastWindow : Window
{
    private const int ScreenMargin = 24;
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromSeconds(2.2) };
    private IntPtr _hwnd;

    public ToastWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.UpdateExStyle(_hwnd,
                NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED,
                NativeMethods.WS_EX_APPWINDOW);
            NativeMethods.SetWindowDisplayAffinity(_hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
        };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
            fade.Completed += (_, _) => { if (Opacity == 0) Hide(); };
            BeginAnimation(OpacityProperty, fade);
        };
    }

    /// <summary>Shows the toast at the bottom-right of the monitor that shows <paramref name="nearWindow"/>.</summary>
    public void Show(string title, string detail, BitmapSource? thumbnail, IntPtr nearWindow, bool isError = false)
    {
        TitleText.Text = title;
        DetailText.Text = detail;
        Thumb.Source = thumbnail;
        ThumbFrame.Visibility = thumbnail is null ? Visibility.Collapsed : Visibility.Visible;
        Glyph.Text = isError ? "" : "";
        Glyph.Foreground = (System.Windows.Media.Brush)FindResource(isError ? "DangerBrush" : "AccentBrush");

        if (!IsVisible)
        {
            Left = -32000;
            Show();
        }
        UpdateLayout();

        var (work, _) = NativeMethods.GetMonitorForWindow(nearWindow);
        NativeMethods.GetWindowRect(_hwnd, out var rect);
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST,
            work.Right - rect.Width - ScreenMargin, work.Bottom - rect.Height - ScreenMargin, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        _hideTimer.Stop();
        _hideTimer.Start();
    }
}
