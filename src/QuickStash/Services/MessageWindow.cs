using System.Windows.Interop;
using QuickStash.Interop;

namespace QuickStash.Services;

/// <summary>
/// A hidden top-level window that receives WM_HOTKEY and tray-icon callbacks.
/// It is a real (invisible) top-level window rather than a message-only one so it also receives the
/// "TaskbarCreated" broadcast that is sent when Explorer restarts.
/// </summary>
internal sealed class MessageWindow : IDisposable
{
    private readonly HwndSource _source;

    public MessageWindow()
    {
        var parameters = new HwndSourceParameters("QuickStashMessageWindow")
        {
            Width = 0,
            Height = 0,
            WindowStyle = NativeMethods.WS_POPUP,
            ExtendedWindowStyle = (int)NativeMethods.WS_EX_TOOLWINDOW,
        };
        _source = new HwndSource(parameters);
    }

    public IntPtr Handle => _source.Handle;

    public void AddHook(HwndSourceHook hook) => _source.AddHook(hook);

    public void Dispose() => _source.Dispose();
}
