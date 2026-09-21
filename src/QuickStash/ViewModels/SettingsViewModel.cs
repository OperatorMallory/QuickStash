using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickStash.Models;

namespace QuickStash.ViewModels;

/// <summary>Editable copy of the settings. Nothing is applied until Save (opacity previews live and is reverted on Cancel).</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _original;
    private readonly Func<AppSettings, string?> _apply;
    private readonly Action<AppSettings> _preview;

    /// <param name="apply">Validates and applies settings; returns an error message, or null on success.</param>
    /// <param name="preview">Shows unsaved visual changes (opacity) immediately.</param>
    internal SettingsViewModel(AppSettings current, bool startWithWindows, string dataFolder,
        Func<AppSettings, string?> apply, Action<AppSettings> preview)
    {
        _original = current.Clone();
        _apply = apply;
        _preview = preview;
        DataFolder = dataFolder;

        _overlayHotkey = current.OverlayHotkey;
        _unpinAllHotkey = current.UnpinAllHotkey;
        _captureHotkey = current.CaptureHotkey;
        _overlayOpacityPercent = Math.Round(current.OverlayOpacity * 100);
        _pinnedOpacityPercent = Math.Round(current.PinnedOpacity * 100);
        _startWithWindows = startWithWindows;
        _autoCaptureScreenshot = current.AutoCaptureScreenshot;
        _hidePinnedFromCapture = current.HidePinnedFromCapture;
    }

    public string DataFolder { get; }

    public event EventHandler? CloseRequested;

    [ObservableProperty] private string _overlayHotkey;
    [ObservableProperty] private string _unpinAllHotkey;
    [ObservableProperty] private string _captureHotkey;
    [ObservableProperty] private double _overlayOpacityPercent;
    [ObservableProperty] private double _pinnedOpacityPercent;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _autoCaptureScreenshot;
    [ObservableProperty] private bool _hidePinnedFromCapture;
    [ObservableProperty] private string? _errorMessage;

    partial void OnOverlayOpacityPercentChanged(double value) => _preview(Build());
    partial void OnPinnedOpacityPercentChanged(double value) => _preview(Build());

    // Start from the current settings so values not shown here (e.g. companion window layout) are kept.
    private AppSettings Build()
    {
        var settings = _original.Clone();
        settings.OverlayHotkey = OverlayHotkey;
        settings.UnpinAllHotkey = UnpinAllHotkey;
        settings.CaptureHotkey = CaptureHotkey;
        settings.OverlayOpacity = OverlayOpacityPercent / 100.0;
        settings.PinnedOpacity = PinnedOpacityPercent / 100.0;
        settings.StartWithWindows = StartWithWindows;
        settings.AutoCaptureScreenshot = AutoCaptureScreenshot;
        settings.HidePinnedFromCapture = HidePinnedFromCapture;
        return settings;
    }


    /// <summary>True once the settings were applied; otherwise closing the window reverts the preview.</summary>
    public bool IsSaved { get; private set; }

    [RelayCommand]
    private void Save()
    {
        ErrorMessage = _apply(Build());
        if (ErrorMessage is not null) return;
        IsSaved = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Called when the window closes without saving: undo the live preview.</summary>
    internal void Revert() => _preview(_original);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ResetHotkeys()
    {
        OverlayHotkey = HotkeyGesture.DefaultOverlay.ToString();
        UnpinAllHotkey = HotkeyGesture.DefaultUnpinAll.ToString();
        CaptureHotkey = HotkeyGesture.DefaultCapture.ToString();
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{DataFolder}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ErrorMessage = "Could not open the folder: " + ex.Message;
        }
    }
}
