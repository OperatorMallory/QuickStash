using QuickStash.Data;
using QuickStash.Interop;
using QuickStash.Models;
using QuickStash.ViewModels;
using QuickStash.Views;

namespace QuickStash.Services;

/// <summary>
/// Owns the pinned-note windows: creates them from the database at startup, keeps them click-through while the
/// overlay is closed and draggable while it is open, saves their positions, and keeps them above the game.
/// </summary>
internal sealed class PinManager : IPinService, IDisposable
{
    private const double DefaultWidth = 280;
    private const int ScreenMargin = 24;
    private const int CascadeStep = 28;

    private readonly NoteRepository _notes;
    private readonly TopicRepository _topics;
    private readonly ImageStore _images;
    private readonly Dictionary<long, PinnedNoteWindow> _windows = new();
    private bool _interactive;
    private double _opacity = 0.85;
    private bool _excludeFromCapture = true;

    public PinManager(NoteRepository notes, TopicRepository topics, ImageStore images, ForegroundWatcher foreground)
    {
        _notes = notes;
        _topics = topics;
        _images = images;
        // Some games re-assert their own z-order when they get focus; re-raise our pins whenever the foreground changes.
        foreground.AnyChange += (_, _) => BringAllToTop();
    }

    /// <summary>Lets the overlay list reflect pin changes made elsewhere (unpin button on the note, "unpin all").</summary>
    public Action<long, bool>? PinStateChanged { get; set; }

    public int Count => _windows.Count;

    public double Opacity
    {
        get => _opacity;
        set
        {
            _opacity = Math.Clamp(value, 0.2, 1.0);
            foreach (var window in _windows.Values) window.Opacity = _opacity;
        }
    }

    public bool ExcludeFromCapture
    {
        get => _excludeFromCapture;
        set
        {
            _excludeFromCapture = value;
            foreach (var window in _windows.Values)
            {
                window.ExcludeFromCapture = value;
                window.ApplyCaptureExclusion();
            }
        }
    }

    /// <summary>Recreates the windows of notes that were pinned when the app last exited.</summary>
    public void RestoreAll()
    {
        foreach (var note in _notes.GetPinned()) Open(note);
    }

    // ───────── IPinService (called by the overlay) ─────────

    public void SetPinned(Note note, bool pinned)
    {
        if (pinned) Open(note);
        else Close(note.Id);
    }

    public void NoteChanged(Note note)
    {
        if (!_windows.TryGetValue(note.Id, out var window)) return;
        window.ViewModel.Text = note.Text;
        _ = LoadImageAsync(window, note.ImagePath);
    }

    public void NoteDeleted(long noteId)
    {
        Close(noteId);
    }

    // ───────── Commands ─────────

    /// <summary>Unpins one note (from the pinned window's own button).</summary>
    public void Unpin(long noteId)
    {
        _notes.SetPinned(noteId, false);
        Close(noteId);
        PinStateChanged?.Invoke(noteId, false);
    }

    /// <summary>Unpins everything (hotkey / tray menu).</summary>
    public int UnpinAll()
    {
        int count = _notes.UnpinAll();
        foreach (var id in _windows.Keys.ToList())
        {
            Close(id);
            PinStateChanged?.Invoke(id, false);
        }
        Log.Info($"Unpinned all ({count})");
        return count;
    }

    /// <summary>Overlay opened (true): windows become draggable and sit above the overlay. Overlay closed (false): click-through again.</summary>
    public void SetInteractive(bool interactive)
    {
        _interactive = interactive;
        foreach (var window in _windows.Values) window.SetInteractive(interactive);
    }

    // ───────── Windows ─────────

    private void Open(Note note)
    {
        if (_windows.ContainsKey(note.Id)) return;

        string topicName = _topics.Get(note.TopicId)?.Name ?? string.Empty;
        var viewModel = new PinnedNoteViewModel(note, topicName, Unpin);
        var window = new PinnedNoteWindow(viewModel)
        {
            Opacity = _opacity,
            ExcludeFromCapture = _excludeFromCapture,
        };
        window.Width = Math.Clamp(note.PinWidth ?? DefaultWidth, window.MinWidth, PinnedNoteWindow.MaxPinWidth);
        window.Moved += (_, position) => _notes.SetPinPosition(note.Id, position.X, position.Y);
        window.Resized += (_, width) =>
        {
            _notes.SetPinWidth(note.Id, width);
            _ = LoadImageAsync(window, _notes.Get(note.Id)?.ImagePath); // sharper image for a bigger window
        };
        _windows[note.Id] = window;

        // Start off-screen, then place once the real (DPI-scaled) size is known.
        window.Left = -32000;
        window.Top = -32000;
        window.Show();
        window.SetInteractive(_interactive);
        Place(window, note);
        _ = LoadImageAsync(window, note.ImagePath);
    }

    private void Place(PinnedNoteWindow window, Note note)
    {
        var (_, _, width, _) = window.GetBounds();
        if (note.PinX is double px && note.PinY is double py
            && NativeMethods.IsOnAnyMonitor((int)px + 20, (int)py + 10)) // saved spot still on a connected monitor
        {
            window.MoveTo((int)px, (int)py);
            return;
        }

        // New pin: cascade down from the top-right of the monitor the game is on.
        var (work, _) = NativeMethods.GetMonitorForWindow(NativeMethods.GetForegroundWindow());
        int index = _windows.Count - 1;
        int x = work.Right - width - ScreenMargin - index * CascadeStep;
        int y = work.Top + ScreenMargin + 60 + index * CascadeStep;
        window.MoveTo(x, y);
        _notes.SetPinPosition(note.Id, x, y);
    }

    private void Close(long noteId)
    {
        if (!_windows.Remove(noteId, out var window)) return;
        window.Close();
    }

    /// <summary>Decodes the image at the pixel width the window actually shows (large pins stay sharp, small ones stay cheap).</summary>
    private async Task LoadImageAsync(PinnedNoteWindow window, string? path)
    {
        if (path is null)
        {
            window.ViewModel.Image = null;
            return;
        }
        double scale = window.DpiScale;
        int decodeWidth = (int)Math.Ceiling((window.Width - 20) * scale);
        window.ViewModel.Image = await Task.Run(() => _images.Load(path, decodeWidth));
    }

    public void BringAllToTop()
    {
        foreach (var window in _windows.Values) window.BringToTop();
    }

    public void Dispose()
    {
        foreach (var window in _windows.Values) window.Close();
        _windows.Clear();
    }
}
