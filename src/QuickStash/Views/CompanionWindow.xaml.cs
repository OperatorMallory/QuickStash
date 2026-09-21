using System.Globalization;
using System.Windows;
using System.Windows.Input;
using QuickStash.ViewModels;

namespace QuickStash.Views;

/// <summary>
/// The second-monitor window: a normal, resizable window with the same notes UI as the overlay. It stays open, follows
/// the game in front, and updates live when captures arrive. Closing it only hides it.
/// </summary>
public partial class CompanionWindow : Window
{
    public CompanionWindow()
    {
        InitializeComponent();
        KeyDown += OnWindowKeyDown;
        StateChanged += (_, _) => OnStateChanged();
        Notes.CloseRequested += (_, _) => Hide();
        Activated += (_, _) => Notes.ApplyPendingFocus();
    }

    // These live inside the NotesView header (its own name scope), so they are picked up when loaded.
    private System.Windows.Controls.Primitives.ToggleButton? _topmostToggle;
    private System.Windows.Controls.Button? _maximizeButton;
    private bool _keepOnTop;

    public bool KeepOnTop
    {
        get => _keepOnTop;
        set
        {
            _keepOnTop = value;
            Topmost = value;
            if (_topmostToggle is not null) _topmostToggle.IsChecked = value;
        }
    }

    private void OnTopmostLoaded(object sender, RoutedEventArgs e)
    {
        _topmostToggle = (System.Windows.Controls.Primitives.ToggleButton)sender;
        _topmostToggle.IsChecked = _keepOnTop;
    }

    private void OnMaximizeLoaded(object sender, RoutedEventArgs e)
    {
        _maximizeButton = (System.Windows.Controls.Button)sender;
        OnStateChanged();
    }

    /// <summary>Raised when the user changes size/position/on-top so the host can remember it.</summary>
    public event EventHandler? LayoutChanged;

    private void OnTopmostChanged(object sender, RoutedEventArgs e)
    {
        bool value = ((System.Windows.Controls.Primitives.ToggleButton)sender).IsChecked == true;
        if (value == _keepOnTop) return;
        _keepOnTop = value;
        Topmost = value;
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        (DataContext as OverlayViewModel)?.HandleEscape();
        e.Handled = true;
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnStateChanged()
    {
        // A chrome-less maximized window overhangs the screen by its resize border; pad it back in.
        Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        if (_maximizeButton is null) return;
        _maximizeButton.Content = WindowState == WindowState.Maximized ? "" : "";
        _maximizeButton.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximize";
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>"left,top,width,height" in device-independent pixels (normal, non-maximized bounds).</summary>
    public string SaveBounds()
    {
        var r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        return string.Join(",", new[] { r.Left, r.Top, r.Width, r.Height }.Select(v => Math.Round(v).ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>Restores saved bounds if they are still on a visible screen area; otherwise keeps the default centered size.</summary>
    public void ApplySavedBounds(string? bounds)
    {
        var parts = bounds?.Split(',');
        if (parts is not { Length: 4 }) return;
        var values = parts.Select(p => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN).ToArray();
        if (values.Any(double.IsNaN)) return;

        var rect = new Rect(values[0], values[1], Math.Max(values[2], MinWidth), Math.Max(values[3], MinHeight));
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (!screen.IntersectsWith(new Rect(rect.Left + 40, rect.Top, 120, 40))) return; // title bar must be reachable

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = rect.Left;
        Top = rect.Top;
        Width = rect.Width;
        Height = rect.Height;
    }
}
