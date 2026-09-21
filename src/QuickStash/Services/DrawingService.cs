using System.Windows;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuickStash.Data;
using QuickStash.Models;

namespace QuickStash.Services;

/// <summary>
/// Drawing on a note's screenshot. The untouched screenshot is always kept; the drawing is saved twice: flattened into a
/// PNG (what lists, pins and the viewer show) and as ink strokes, so it can be edited again later.
/// </summary>
internal sealed class DrawingService
{
    private readonly NoteRepository _notes;
    private readonly ImageStore _images;
    private readonly DataChanges _changes;

    public DrawingService(NoteRepository notes, ImageStore images, DataChanges changes)
    {
        _notes = notes;
        _images = images;
        _changes = changes;
    }

    /// <summary>Raised after a note's image changed because of a drawing (so pinned windows can refresh).</summary>
    public event EventHandler<Note>? NoteImageChanged;

    /// <summary>What the editor needs: the clean screenshot and any strokes drawn on it before.</summary>
    public (BitmapSource Image, StrokeCollection Strokes)? Open(long noteId)
    {
        var note = _notes.Get(noteId);
        string? original = note?.OriginalImagePath ?? note?.ImagePath;
        var image = _images.Load(original);
        if (note is null || image is null) return null;
        return (image, _images.LoadStrokes(note.AnnotationPath));
    }

    /// <summary>Saves the drawing. With no strokes left, the note goes back to its original screenshot.</summary>
    public void Save(long noteId, BitmapSource original, StrokeCollection strokes)
    {
        if (strokes.Count == 0)
        {
            Revert(noteId);
            return;
        }

        string drawn = _images.Save(Flatten(original, strokes));
        string ink = _images.SaveStrokes(strokes);
        _images.Delete(_notes.SetDrawing(noteId, drawn, ink));
        Notify(noteId);
    }

    /// <summary>Drops the drawing and shows the original screenshot again.</summary>
    public void Revert(long noteId)
    {
        _images.Delete(_notes.RemoveDrawing(noteId));
        Notify(noteId);
    }

    private void Notify(long noteId)
    {
        if (_notes.Get(noteId) is not { } note) return;
        _changes.Raise(this, note.TopicId);
        NoteImageChanged?.Invoke(this, note);
    }

    /// <summary>Renders the screenshot with the strokes on top, at the screenshot's own pixel size.</summary>
    internal static BitmapSource Flatten(BitmapSource original, StrokeCollection strokes)
    {
        int width = original.PixelWidth, height = original.PixelHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(original, new Rect(0, 0, width, height));
            strokes.Draw(dc);
        }
        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }
}
