using System.Diagnostics;
using System.Runtime;
using System.Windows.Threading;
using QuickStash.Interop;
using QuickStash.ViewModels;
using QuickStash.Views;

namespace QuickStash.Services;

/// <summary>Owns the overlay's open/close lifecycle. The window is created once and only ever shown/hidden.</summary>
internal sealed class OverlayController
{
    private readonly OverlayWindow _window;
    private readonly OverlayViewModel _viewModel;
    private readonly ForegroundCaptureService _capture;
    private readonly Func<bool> _autoCapture;

    public OverlayController(OverlayWindow window, OverlayViewModel viewModel, ForegroundCaptureService capture, Func<bool> autoCapture)
    {
        _window = window;
        _viewModel = viewModel;
        _capture = capture;
        _autoCapture = autoCapture;
        _window.DataContext = viewModel;
        _window.CloseRequested += (_, _) => Hide();
    }

    public bool IsOpen => _window.IsVisible;

    public event EventHandler? Opened;
    public event EventHandler? Closed;

    public void Toggle()
    {
        if (IsOpen) Hide();
        else Show();
    }

    public void Show()
    {
        if (IsOpen)
        {
            _window.Activate();
            return;
        }

        var stopwatch = Stopwatch.StartNew();

        // Detect + capture BEFORE the overlay appears, so it is never part of the screenshot.
        ForegroundInfo foreground = _capture.Capture(_autoCapture());
        _viewModel.Prepare(foreground);

        _window.ShowCentered(foreground.Window);
        Opened?.Invoke(this, EventArgs.Empty);

        _window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            Log.Info($"Overlay visible in {stopwatch.ElapsedMilliseconds} ms (process: {foreground.ProcessName ?? "none"})"));
    }

    public void Hide()
    {
        if (!IsOpen) return;
        _window.Hide();
        _viewModel.OnHidden();
        Closed?.Invoke(this, EventArgs.Empty);

        // A full-window screenshot can be tens of MB; give it back to the OS once we're idle in the tray again.
        _window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, ReleaseIdleMemory);
    }

    /// <summary>Compacts the heap (screenshots live on the large-object heap) and trims the working set.</summary>
    public static void ReleaseIdleMemory()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        NativeMethods.TrimWorkingSet();
    }
}
