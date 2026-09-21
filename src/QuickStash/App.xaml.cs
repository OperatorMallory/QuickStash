using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using QuickStash.Data;
using QuickStash.Models;
using QuickStash.Services;
using QuickStash.ViewModels;
using QuickStash.Views;

namespace QuickStash;

/// <summary>Composition root: wires services together. No window is shown at startup; the app lives in the tray.</summary>
public partial class App : Application
{
    private Mutex? _singleInstance;
    private MessageWindow? _messageWindow;
    private HotkeyService? _hotkeys;
    private TrayIconService? _tray;
    private OverlayController? _overlay;
    private OverlayWindow? _overlayWindow;
    private PinManager? _pins;
    private SettingsService? _settings;
    private Database? _database;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(true, @"Local\QuickStash.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        base.OnStartup(e);
        AppPaths.EnsureCreated();
        Log.Initialize(AppPaths.DataFolder);
        Log.Info($"QuickStash {typeof(App).Assembly.GetName().Version} starting (data: {AppPaths.DataFolder})");
        DispatcherUnhandledException += OnUnhandledException;

        // Software rendering: our windows are small layered windows, so WPF reads GPU output back to the CPU anyway.
        // Skipping the Direct3D device saves ~50 MB and keeps QuickStash off the game's GPU.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        _settings = new SettingsService(AppPaths.SettingsPath);
        _messageWindow = new MessageWindow();
        _hotkeys = new HotkeyService(_messageWindow);

        // Storage
        _database = new Database(AppPaths.DatabasePath);
        var topics = new TopicRepository(_database);
        var notes = new NoteRepository(_database);
        var images = new ImageStore(AppPaths.DataFolder);

        // Overlay (created once, then only shown/hidden)
        var overlayViewModel = new OverlayViewModel(topics, notes, images);
        _overlayWindow = new OverlayWindow();
        _overlay = new OverlayController(_overlayWindow, overlayViewModel, new ForegroundCaptureService(),
            () => _settings.Current.AutoCaptureScreenshot);
        _overlayWindow.WarmUp();

        // Pinned notes
        _pins = new PinManager(notes, topics, images);
        overlayViewModel.Pins = _pins;
        _pins.PinStateChanged = overlayViewModel.SyncPinState;
        _overlay.Opened += (_, _) => _pins.SetInteractive(true);
        _overlay.Closed += (_, _) => _pins.SetInteractive(false);

        // Tray
        _tray = new TrayIconService(_messageWindow, LoadIconBytes(), "QuickStash");
        _tray.ContextMenu = BuildTrayMenu();
        _tray.Activated += (_, _) => _overlay.Show();
        _tray.Show();

        overlayViewModel.SettingsRequested += (_, _) => OpenSettings();
        _settings.Changed += (_, settings) => ApplyVisuals(settings);
        ApplyVisuals(_settings.Current);

        // Keep the Run entry pointing at this exe if the app was moved.
        if (StartupRegistration.IsEnabled()) StartupRegistration.SetEnabled(true);

        var failed = RegisterHotkeys(_settings.Current);
        if (failed.Count > 0)
            _tray.ShowBalloon("Hotkey unavailable", $"{string.Join(" and ", failed)} is used by another application. Pick another in Settings.", warning: true);
        else
            _tray.ShowBalloon("QuickStash is running", $"Press {_settings.Current.GetOverlayHotkey()} to open the overlay.");

        _pins.RestoreAll();

        // Once startup work is done, hand unused memory back so the tray app idles small.
        var idle = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        idle.Tick += (_, _) =>
        {
            idle.Stop();
            if (!_overlay.IsOpen) OverlayController.ReleaseIdleMemory();
        };
        idle.Start();
    }

    private void ApplyVisuals(AppSettings settings)
    {
        _overlayWindow!.Opacity = settings.OverlayOpacity;
        _pins!.Opacity = settings.PinnedOpacity;
        _pins.ExcludeFromCapture = settings.HidePinnedFromCapture;
    }

    /// <summary>(Re)registers both global hotkeys. Returns the combinations that could not be registered.</summary>
    private List<string> RegisterHotkeys(AppSettings settings)
    {
        var overlayHotkey = settings.GetOverlayHotkey();
        var unpinHotkey = settings.GetUnpinAllHotkey();
        var failed = new List<string>();
        if (!_hotkeys!.Register(HotkeyService.OverlayHotkeyId, overlayHotkey, _overlay!.Toggle)) failed.Add(overlayHotkey.ToString());
        if (!_hotkeys.Register(HotkeyService.UnpinAllHotkeyId, unpinHotkey, () => _pins!.UnpinAll())) failed.Add(unpinHotkey.ToString());
        return failed;
    }

    // ───────── Settings window ─────────

    private SettingsWindow? _settingsWindow;

    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }
        _overlay!.Hide();

        // Release the hotkeys while the window is open so the hotkey boxes can record them.
        _hotkeys!.UnregisterAll();

        var viewModel = new SettingsViewModel(_settings!.Current, StartupRegistration.IsEnabled(), AppPaths.DataFolder,
            apply: TryApplySettings, preview: ApplyVisuals);
        _settingsWindow = new SettingsWindow(viewModel);
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            var failed = RegisterHotkeys(_settings.Current);
            if (failed.Count > 0)
                _tray!.ShowBalloon("Hotkey unavailable", $"{string.Join(" and ", failed)} is used by another application.", warning: true);
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>Validates and applies new settings. Returns an error message for the settings window, or null on success.</summary>
    private string? TryApplySettings(AppSettings settings)
    {
        if (!HotkeyGesture.TryParse(settings.OverlayHotkey, out var overlayHotkey) ||
            !HotkeyGesture.TryParse(settings.UnpinAllHotkey, out var unpinHotkey))
            return "Each hotkey needs a modifier (Ctrl, Alt, Shift or Win) and a key.";
        if (overlayHotkey == unpinHotkey)
            return "The two hotkeys must be different.";

        // Probe the new combinations; they are registered for real when the window closes.
        var failed = RegisterHotkeys(settings);
        _hotkeys!.UnregisterAll();
        if (failed.Count > 0)
            return $"{string.Join(" and ", failed)} is already used by another application. Choose a different combination.";

        StartupRegistration.SetEnabled(settings.StartWithWindows);
        _settings!.Save(settings);
        Log.Info("Settings saved");
        return null;
    }

    private ContextMenu BuildTrayMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(MenuItem("Open QuickStash", () => _overlay!.Show(), bold: true));
        menu.Items.Add(MenuItem("Unpin all notes", () => _pins!.UnpinAll()));
        menu.Items.Add(MenuItem("Settings…", OpenSettings));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("Exit", Shutdown));
        return menu;
    }

    private static MenuItem MenuItem(string header, Action action, bool bold = false)
    {
        var item = new MenuItem { Header = header, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
        item.Click += (_, _) => action();
        return item;
    }

    private static byte[] LoadIconBytes()
    {
        var info = GetResourceStream(new Uri("pack://application:,,,/Assets/QuickStash.ico"))!;
        using var stream = info.Stream;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled exception", e.Exception);
        e.Handled = true; // stay alive in the tray rather than crash mid-game
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeys?.Dispose();
        _pins?.Dispose();
        _tray?.Dispose();
        _messageWindow?.Dispose();
        _database?.Dispose();
        if (_singleInstance is not null)
        {
            _singleInstance.ReleaseMutex();
            _singleInstance.Dispose();
        }
        Log.Info("QuickStash exited");
        base.OnExit(e);
    }
}
