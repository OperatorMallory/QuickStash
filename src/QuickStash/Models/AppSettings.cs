namespace QuickStash.Models;

/// <summary>User settings, stored as JSON in the data folder. Hotkeys are stored as text like "Ctrl+Shift+Space".</summary>
public sealed class AppSettings
{
    public string OverlayHotkey { get; set; } = HotkeyGesture.DefaultOverlay.ToString();
    public string UnpinAllHotkey { get; set; } = HotkeyGesture.DefaultUnpinAll.ToString();

    /// <summary>Screenshot the game straight into its topic, without opening anything.</summary>
    public string CaptureHotkey { get; set; } = HotkeyGesture.DefaultCapture.ToString();

    /// <summary>Opacity of the overlay window, 0.5–1.</summary>
    public double OverlayOpacity { get; set; } = 0.97;

    /// <summary>Opacity of pinned notes, 0.2–1.</summary>
    public double PinnedOpacity { get; set; } = 0.85;

    public bool StartWithWindows { get; set; }

    /// <summary>Capture a screenshot of the game every time the overlay opens.</summary>
    public bool AutoCaptureScreenshot { get; set; } = true;

    /// <summary>Hide pinned notes from screenshots and screen recordings/streams (Windows 10 2004+).</summary>
    public bool HidePinnedFromCapture { get; set; } = true;

    /// <summary>The first-run introduction has been finished or skipped.</summary>
    public bool OnboardingCompleted { get; set; }

    // Companion (second-monitor) window, remembered between sessions.
    public bool CompanionOpen { get; set; }
    public string? CompanionBounds { get; set; }
    public bool CompanionTopmost { get; set; }
    public bool CompanionFollowGame { get; set; } = true;

    public HotkeyGesture GetCaptureHotkey() =>
        HotkeyGesture.TryParse(CaptureHotkey, out var g) ? g : HotkeyGesture.DefaultCapture;

    public HotkeyGesture GetOverlayHotkey() =>
        HotkeyGesture.TryParse(OverlayHotkey, out var g) ? g : HotkeyGesture.DefaultOverlay;

    public HotkeyGesture GetUnpinAllHotkey() =>
        HotkeyGesture.TryParse(UnpinAllHotkey, out var g) ? g : HotkeyGesture.DefaultUnpinAll;

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    /// <summary>Clamps values that may have been hand-edited in the JSON file.</summary>
    public void Normalize()
    {
        OverlayOpacity = Math.Clamp(double.IsFinite(OverlayOpacity) ? OverlayOpacity : 0.97, 0.5, 1.0);
        PinnedOpacity = Math.Clamp(double.IsFinite(PinnedOpacity) ? PinnedOpacity : 0.85, 0.2, 1.0);
        if (!HotkeyGesture.TryParse(OverlayHotkey, out _)) OverlayHotkey = HotkeyGesture.DefaultOverlay.ToString();
        if (!HotkeyGesture.TryParse(UnpinAllHotkey, out _)) UnpinAllHotkey = HotkeyGesture.DefaultUnpinAll.ToString();
        if (!HotkeyGesture.TryParse(CaptureHotkey, out _)) CaptureHotkey = HotkeyGesture.DefaultCapture.ToString();
    }
}
