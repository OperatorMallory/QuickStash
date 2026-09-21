using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuickStash.Interop;

namespace QuickStash.Services;

/// <summary>What was in front when the hotkey was pressed.</summary>
/// <param name="Window">The foreground window, or zero when it was the desktop/taskbar/QuickStash itself.</param>
/// <param name="ProcessName">Lowercase executable name such as "tld.exe", or null.</param>
/// <param name="Screenshot">Frozen in-memory capture of the window, or null when disabled/unavailable.</param>
internal sealed record ForegroundInfo(IntPtr Window, string? ProcessName, BitmapSource? Screenshot)
{
    public static readonly ForegroundInfo None = new(IntPtr.Zero, null, null);
}

/// <summary>Detects the foreground process and captures its window. Runs before the overlay is shown so the overlay is never in the shot.</summary>
internal sealed class ForegroundCaptureService
{
    // Shell surfaces that are never a "game": taskbar, desktop, tray overflow.
    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW",
        "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland", "Windows.UI.Core.CoreWindow",
    };

    /// <summary>Describes (and optionally captures) whatever window is in front right now.</summary>
    public ForegroundInfo Capture(bool includeScreenshot) => Describe(NativeMethods.GetForegroundWindow(), includeScreenshot);

    /// <summary>
    /// Describes (and optionally captures) a specific window, e.g. the game the companion window last saw in front.
    /// Returns <see cref="ForegroundInfo.None"/> for shell windows, QuickStash's own windows, or windows that are gone.
    /// </summary>
    public ForegroundInfo Describe(IntPtr hwnd, bool includeScreenshot)
    {
        if (!NativeMethods.IsWindow(hwnd) || !IsCandidate(hwnd)) return ForegroundInfo.None;

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0 || pid == Environment.ProcessId) return ForegroundInfo.None;

        string? processName = GetProcessName(pid);
        BitmapSource? screenshot = null;
        if (includeScreenshot)
        {
            var stopwatch = Stopwatch.StartNew();
            screenshot = WithOwnWindowsExcluded(() => CaptureWindow(hwnd));
            Log.Info($"Captured {processName} {screenshot?.PixelWidth}x{screenshot?.PixelHeight} in {stopwatch.ElapsedMilliseconds} ms");
        }
        return new ForegroundInfo(hwnd, processName, screenshot);
    }

    private static bool IsCandidate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || hwnd == NativeMethods.GetShellWindow() || hwnd == NativeMethods.GetDesktopWindow())
            return false;
        if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd))
            return false;
        return !IgnoredClasses.Contains(NativeMethods.GetWindowClassName(hwnd));
    }

    /// <summary>"tld.exe" style name. Process.ProcessName works even for elevated processes, unlike MainModule.</summary>
    internal static string? GetProcessName(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return NormalizeProcessName(process.ProcessName);
        }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    public static string NormalizeProcessName(string name)
    {
        name = name.Trim().ToLowerInvariant();
        return name.EndsWith(".exe", StringComparison.Ordinal) ? name : name + ".exe";
    }

    /// <summary>
    /// The screen copy grabs whatever is visible where the game is. If one of our own windows overlaps it (e.g. the
    /// companion window on the same monitor), briefly exclude our windows from capture so the shot shows the game.
    /// </summary>
    private static T WithOwnWindowsExcluded<T>(Func<T> capture)
    {
        var changed = new List<IntPtr>();
        // Every visible window we own, including tooltip/menu popups (a tooltip over the game would end up in the shot).
        foreach (IntPtr handle in NativeMethods.GetOwnVisibleWindows())
        {
            if (NativeMethods.GetWindowDisplayAffinity(handle, out uint affinity) && affinity != NativeMethods.WDA_NONE) continue;
            if (NativeMethods.SetWindowDisplayAffinity(handle, NativeMethods.WDA_EXCLUDEFROMCAPTURE)) changed.Add(handle);
        }
        try
        {
            if (changed.Count > 0) NativeMethods.DwmFlush(); // let the compositor drop our windows from the next frame
            return capture();
        }
        finally
        {
            foreach (var handle in changed) NativeMethods.SetWindowDisplayAffinity(handle, NativeMethods.WDA_NONE);
        }
    }

    private static BitmapSource? CaptureWindow(IntPtr hwnd)
    {
        if (!NativeMethods.TryGetVisibleBounds(hwnd, out var rect)) return null;
        byte[]? pixels = NativeMethods.CaptureScreenPixels(rect);
        if (pixels is null) return null;

        var bitmap = BitmapSource.Create(rect.Width, rect.Height, 96, 96, PixelFormats.Bgr32, null, pixels, rect.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
