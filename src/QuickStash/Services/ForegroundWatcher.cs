using QuickStash.Interop;

namespace QuickStash.Services;

/// <summary>
/// Notifies when another program comes to the front (out-of-context WinEvent hook, nothing is injected into the game)
/// and remembers the last such window, so the companion window can capture "the game" even while it has focus itself.
/// </summary>
internal sealed class ForegroundWatcher : IDisposable
{
    private readonly ForegroundCaptureService _capture;
    private readonly NativeMethods.WinEventProc _callback; // kept alive for the native hook
    private IntPtr _hook;

    public ForegroundWatcher(ForegroundCaptureService capture)
    {
        _capture = capture;
        _callback = OnForegroundChanged;
        _hook = NativeMethods.SetWinEventHook(NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _callback, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
        Last = _capture.Capture(includeScreenshot: false);
    }

    /// <summary>The most recent foreground window that belongs to another program (not the shell, not QuickStash).</summary>
    public ForegroundInfo Last { get; private set; }

    /// <summary>Raised for every foreground change to another program's window (after <see cref="Last"/> is updated).</summary>
    public event EventHandler<ForegroundInfo>? Changed;

    /// <summary>Raised for every foreground change, including to shell windows; used to keep pinned notes on top.</summary>
    public event EventHandler? AnyChange;

    private void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        AnyChange?.Invoke(this, EventArgs.Empty);
        var info = _capture.Describe(hwnd, includeScreenshot: false);
        if (info.Window == IntPtr.Zero) return;
        Last = info;
        Changed?.Invoke(this, info);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) NativeMethods.UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }
}
