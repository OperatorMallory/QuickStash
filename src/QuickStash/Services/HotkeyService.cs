using QuickStash.Interop;
using QuickStash.Models;

namespace QuickStash.Services;

/// <summary>Registers system-wide hotkeys with RegisterHotKey and dispatches WM_HOTKEY to handlers.</summary>
internal sealed class HotkeyService : IDisposable
{
    public const int OverlayHotkeyId = 1;
    public const int UnpinAllHotkeyId = 2;
    public const int CaptureHotkeyId = 3;

    private readonly IntPtr _hwnd;
    private readonly Dictionary<int, Action> _handlers = new();

    public HotkeyService(MessageWindow window)
    {
        _hwnd = window.Handle;
        window.AddHook(WndProc);
    }

    /// <summary>Registers (or re-registers) a hotkey. Returns false when the combination is taken by another app or invalid.</summary>
    public bool Register(int id, HotkeyGesture gesture, Action handler)
    {
        Unregister(id);
        if (!gesture.IsValid) return false;

        if (!NativeMethods.RegisterHotKey(_hwnd, id, gesture.NativeModifiers | NativeMethods.MOD_NOREPEAT, gesture.VirtualKey))
        {
            Log.Error($"RegisterHotKey failed for {gesture} (Win32 error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()})");
            return false;
        }

        _handlers[id] = handler;
        Log.Info($"Registered hotkey {id}: {gesture}");
        return true;
    }

    public void Unregister(int id)
    {
        if (_handlers.Remove(id)) NativeMethods.UnregisterHotKey(_hwnd, id);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && _handlers.TryGetValue(wParam.ToInt32(), out var handler))
        {
            handled = true;
            handler();
        }
        return IntPtr.Zero;
    }

    /// <summary>Releases every hotkey (e.g. while the settings window captures a new combination).</summary>
    public void UnregisterAll()
    {
        foreach (var id in _handlers.Keys.ToList()) Unregister(id);
    }

    public void Dispose() => UnregisterAll();
}
