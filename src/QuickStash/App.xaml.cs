using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using QuickStash.Data;
using QuickStash.Interop;
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
    private ForegroundWatcher? _foreground;
    private QuickCaptureService? _quickCapture;
    private DrawingService? _drawing;

    // Shared by the overlay and the companion window
    private TopicRepository? _topics;
    private NoteRepository? _notes;
    private ImageStore? _images;
    private DataChanges? _changes;

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
        _topics = new TopicRepository(_database);
        _notes = new NoteRepository(_database);
        _images = new ImageStore(AppPaths.DataFolder);
        _changes = new DataChanges();

        // Foreground tracking + capture
        var capture = new ForegroundCaptureService();
        _foreground = new ForegroundWatcher(capture);
        _quickCapture = new QuickCaptureService(capture, _topics, _notes, _images, _changes);
        _drawing = new DrawingService(_notes, _images, _changes);

        // Overlay (created once, then only shown/hidden)
        var overlayViewModel = CreatePanelViewModel();
        _overlayWindow = new OverlayWindow();
        _overlayWindow.Notes.HeaderContent = MakeHeaderButton("", "Open the companion window (for a second monitor)", () =>
        {
            _overlay!.Hide();
            ShowCompanion(activate: true);
        });
        _overlay = new OverlayController(_overlayWindow, overlayViewModel, capture, () => _settings.Current.AutoCaptureScreenshot);
        _overlayWindow.WarmUp();

        // Pinned notes
        _pins = new PinManager(_notes, _topics, _images, _foreground);
        overlayViewModel.Pins = _pins;
        _pins.PinStateChanged = (id, pinned) =>
        {
            overlayViewModel.SyncPinState(id, pinned);
            _companionViewModel?.SyncPinState(id, pinned);
        };
        _drawing.NoteImageChanged += (_, note) => _pins.NoteChanged(note);
        _overlay.Opened += (_, _) => _pins.SetInteractive(true);
        _overlay.Closed += (_, _) => _pins.SetInteractive(false);

        // Companion window follows the game in front
        _foreground.Changed += (_, info) =>
        {
            if (_companionWindow is { IsVisible: true }) _companionViewModel!.Follow(info);
        };

        // Tray
        _tray = new TrayIconService(_messageWindow, LoadIconBytes(), "QuickStash");
        _tray.ContextMenu = BuildTrayMenu();
        _tray.Activated += (_, _) => _overlay.Show();
        _tray.Show();

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
        if (_settings.Current.CompanionOpen) ShowCompanion(activate: false);

        // Once startup work is done, hand unused memory back so the tray app idles small.
        var idle = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        idle.Tick += (_, _) =>
        {
            idle.Stop();
            if (!_overlay.IsOpen) OverlayController.ReleaseIdleMemory();
        };
        idle.Start();
    }

    /// <summary>A notes panel view model (the overlay and the companion window each have one) wired to shared services.</summary>
    private OverlayViewModel CreatePanelViewModel()
    {
        var viewModel = new OverlayViewModel(_topics!, _notes!, _images!, _changes!) { Pins = _pins };
        viewModel.SettingsRequested += (_, _) => OpenSettings();
        viewModel.DrawRequested += (sender, note) => OpenDrawing(note, sender == _companionViewModel ? _companionWindow : null);
        viewModel.CaptureRequested += async (_, _) => await _quickCapture!.CaptureWindowAsync(_foreground!.Last.Window);
        return viewModel;
    }

    private static Button MakeHeaderButton(string glyph, string toolTip, Action action)
    {
        var button = new Button { Content = glyph, ToolTip = toolTip };
        button.SetResourceReference(FrameworkElement.StyleProperty, "IconButton");
        button.Click += (_, _) => action();
        return button;
    }

    private void ApplyVisuals(AppSettings settings)
    {
        _overlayWindow!.Opacity = settings.OverlayOpacity;
        _pins!.Opacity = settings.PinnedOpacity;
        _pins.ExcludeFromCapture = settings.HidePinnedFromCapture;
    }

    /// <summary>(Re)registers the global hotkeys. Returns the combinations that could not be registered.</summary>
    private List<string> RegisterHotkeys(AppSettings settings)
    {
        var failed = new List<string>();
        void Register(int id, HotkeyGesture gesture, Action handler)
        {
            if (!_hotkeys!.Register(id, gesture, handler)) failed.Add(gesture.ToString());
        }
        Register(HotkeyService.OverlayHotkeyId, settings.GetOverlayHotkey(), _overlay!.Toggle);
        Register(HotkeyService.UnpinAllHotkeyId, settings.GetUnpinAllHotkey(), () => _pins!.UnpinAll());
        Register(HotkeyService.CaptureHotkeyId, settings.GetCaptureHotkey(), QuickCapture);
        return failed;
    }

    /// <summary>Quick-capture hotkey. When one of our own windows is in front (e.g. the companion), capture the last game instead.</summary>
    private async void QuickCapture()
    {
        if (_overlay!.IsOpen) return; // the overlay already took a screenshot; don't capture the overlay itself
        NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out uint pid);
        if (pid == Environment.ProcessId) await _quickCapture!.CaptureWindowAsync(_foreground!.Last.Window);
        else await _quickCapture!.CaptureForegroundAsync();
    }

    // ───────── Companion window ─────────

    private CompanionWindow? _companionWindow;
    private OverlayViewModel? _companionViewModel;
    private readonly DispatcherTimer _companionLayoutSave = new() { Interval = TimeSpan.FromSeconds(1) };

    private void ShowCompanion(bool activate)
    {
        if (_companionWindow is null)
        {
            _companionViewModel = CreatePanelViewModel();
            _companionViewModel.IsLive = true;
            _companionViewModel.FollowGame = _settings!.Current.CompanionFollowGame;
            _companionViewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(OverlayViewModel.FollowGame)) SaveCompanionState();
            };

            _companionWindow = new CompanionWindow { DataContext = _companionViewModel };
            _companionWindow.ApplySavedBounds(_settings.Current.CompanionBounds);
            _companionWindow.KeepOnTop = _settings.Current.CompanionTopmost;
            _companionWindow.LayoutChanged += (_, _) =>
            {
                _companionLayoutSave.Stop();
                _companionLayoutSave.Start();
            };
            _companionWindow.IsVisibleChanged += (_, _) => SaveCompanionState();
            _companionLayoutSave.Tick += (_, _) =>
            {
                _companionLayoutSave.Stop();
                SaveCompanionState();
            };
            _companionViewModel.Initialize(_foreground!.Last);
        }

        _companionWindow.ShowActivated = activate;
        _companionWindow.Show();
        if (_companionWindow.WindowState == WindowState.Minimized) _companionWindow.WindowState = WindowState.Normal;
        if (activate) _companionWindow.Activate();
    }

    private void SaveCompanionState()
    {
        if (_companionWindow is null || _settings is null) return;
        var settings = _settings.Current.Clone();
        settings.CompanionOpen = _companionWindow.IsVisible;
        settings.CompanionBounds = _companionWindow.SaveBounds();
        settings.CompanionTopmost = _companionWindow.KeepOnTop;
        settings.CompanionFollowGame = _companionViewModel!.FollowGame;
        _settings.Save(settings);
    }

    // ───────── Drawing on screenshots ─────────

    private readonly Dictionary<long, AnnotationWindow> _drawingWindows = new();

    private void OpenDrawing(Note note, Window? owner)
    {
        if (_drawingWindows.TryGetValue(note.Id, out var existing))
        {
            existing.Activate();
            return;
        }
        if (_drawing!.Open(note.Id) is not { } opened)
        {
            _tray!.ShowBalloon("Can't open image", "The image file for this note is missing.", warning: true);
            return;
        }
        var (image, strokes) = opened;

        string topic = _topics!.Get(note.TopicId)?.Name ?? string.Empty;
        string title = Note.GetTitle(note.Text) is { Length: > 0 } t ? $"{topic}  ·  {t}" : topic;
        var window = new AnnotationWindow(image, strokes, title, note.HasDrawing);
        if (owner is { IsVisible: true })
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        window.SaveRequested += (_, finalStrokes) => _drawing.Save(note.Id, image, finalStrokes);
        window.RevertRequested += (_, _) => _drawing.Revert(note.Id);
        window.Closed += (_, _) => _drawingWindows.Remove(note.Id);
        _drawingWindows[note.Id] = window;

        _overlay!.Hide();
        window.Show();
        window.Activate();
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
        var gestures = new List<HotkeyGesture>();
        foreach (var text in new[] { settings.OverlayHotkey, settings.UnpinAllHotkey, settings.CaptureHotkey })
        {
            if (!HotkeyGesture.TryParse(text, out var gesture))
                return "Each hotkey needs a modifier (Ctrl, Alt, Shift or Win) and a key.";
            gestures.Add(gesture);
        }
        if (gestures.Distinct().Count() != gestures.Count)
            return "Each hotkey must be different.";

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
        menu.Items.Add(MenuItem("Companion window", () => ShowCompanion(activate: true)));
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
        if (_companionWindow is not null && _settings is not null)
        {
            // Remember whether the companion was open (closing the app is not the same as hiding it).
            var settings = _settings.Current.Clone();
            settings.CompanionBounds = _companionWindow.SaveBounds();
            _settings.Save(settings);
        }
        _hotkeys?.Dispose();
        _foreground?.Dispose();
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
