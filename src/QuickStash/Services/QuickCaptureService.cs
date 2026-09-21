using System.Windows.Media.Imaging;
using Microsoft.Data.Sqlite;
using QuickStash.Data;
using QuickStash.Models;
using QuickStash.Views;

namespace QuickStash.Services;

/// <summary>
/// One keypress, no UI: screenshot the game, file it as a note in that game's topic (creating and linking the topic the
/// first time), and show a small toast. The game keeps focus the whole time.
/// </summary>
internal sealed class QuickCaptureService
{
    private const int ToastThumbnailWidth = 160;

    private readonly ForegroundCaptureService _capture;
    private readonly TopicRepository _topics;
    private readonly NoteRepository _notes;
    private readonly ImageStore _images;
    private readonly DataChanges _changes;
    private readonly Lazy<ToastWindow> _toast = new(() => new ToastWindow());
    private bool _busy;

    /// <summary>Raised after a capture was saved.</summary>
    public event EventHandler<Note>? Captured;

    public QuickCaptureService(ForegroundCaptureService capture, TopicRepository topics, NoteRepository notes, ImageStore images, DataChanges changes)
    {
        _capture = capture;
        _topics = topics;
        _notes = notes;
        _images = images;
        _changes = changes;
    }

    /// <summary>Captures the window in front (hotkey).</summary>
    public Task<Note?> CaptureForegroundAsync() => CaptureAsync(_capture.Capture(includeScreenshot: true));

    /// <summary>Captures a specific window, e.g. the game the companion window last saw (its own window has focus then).</summary>
    public Task<Note?> CaptureWindowAsync(IntPtr window) => CaptureAsync(_capture.Describe(window, includeScreenshot: true));

    private async Task<Note?> CaptureAsync(ForegroundInfo target)
    {
        if (_busy) return null; // ignore key repeat while a capture is being saved
        if (target.Screenshot is null)
        {
            _toast.Value.Show("Nothing to capture", "Bring the game to the front first", null, target.Window, isError: true);
            return null;
        }

        _busy = true;
        try
        {
            var topic = target.ProcessName is not null
                ? _topics.GetOrCreateForProcess(target.ProcessName)
                : _topics.GetOrCreateInbox();
            string path = await _images.SaveAsync(target.Screenshot);
            var note = _notes.Add(topic.Id, string.Empty, path);
            _topics.Touch(topic.Id);
            _changes.Raise(this, topic.Id);

            Log.Info($"Quick capture saved to {topic.Name} ({path})");
            _toast.Value.Show($"Saved to {topic.Name}", "Screenshot added to your notes", MakeThumbnail(target.Screenshot), target.Window);
            Captured?.Invoke(this, note);
            return note;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
        {
            Log.Error("Quick capture failed", ex);
            _toast.Value.Show("Capture failed", ex.Message, null, target.Window, isError: true);
            return null;
        }
        finally
        {
            _busy = false;
        }
    }

    private static BitmapSource MakeThumbnail(BitmapSource source)
    {
        double scale = Math.Min(1.0, (double)ToastThumbnailWidth / source.PixelWidth);
        var small = new TransformedBitmap(source, new System.Windows.Media.ScaleTransform(scale, scale));
        var copy = new WriteableBitmap(small); // detach from the full-size screenshot so it can be collected
        copy.Freeze();
        return copy;
    }
}
