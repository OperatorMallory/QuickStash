using Microsoft.Data.Sqlite;
using QuickStash.Models;

namespace QuickStash.Data;

internal sealed class NoteRepository
{
    private const string Columns =
        "n.Id, n.TopicId, n.Text, n.ImagePath, n.IsPinned, n.PinX, n.PinY, n.CreatedAt, n.UpdatedAt, " +
        "n.OriginalImagePath, n.AnnotationPath, n.PinWidth";
    private const int ColumnCount = 12;

    private readonly Database _db;

    public NoteRepository(Database db) => _db = db;

    /// <summary>Notes of a topic, newest first.</summary>
    public List<Note> GetByTopic(long topicId) =>
        _db.Query($"SELECT {Columns} FROM Notes n WHERE n.TopicId = @topic ORDER BY n.CreatedAt DESC, n.Id DESC", Map, ("@topic", topicId));

    public Note? Get(long id) =>
        _db.Query($"SELECT {Columns} FROM Notes n WHERE n.Id = @id", Map, ("@id", id)).FirstOrDefault();

    public Note Add(long topicId, string text, string? imagePath)
    {
        var now = Database.ToDb(DateTime.UtcNow);
        using var cmd = _db.Command("""
            INSERT INTO Notes (TopicId, Text, ImagePath, CreatedAt, UpdatedAt) VALUES (@topic, @text, @image, @now, @now);
            SELECT last_insert_rowid();
            """, null, ("@topic", topicId), ("@text", text), ("@image", imagePath), ("@now", now));
        long id = (long)cmd.ExecuteScalar()!;
        return Get(id)!;
    }

    public void UpdateText(long id, string text) =>
        _db.Execute("UPDATE Notes SET Text = @text, UpdatedAt = @now WHERE Id = @id",
            ("@text", text), ("@now", Now()), ("@id", id));

    /// <summary>Removes the note's image (and any drawing on it). Returns the files that are no longer referenced.</summary>
    public List<string> ClearImage(long id)
    {
        var files = Get(id)?.Files().ToList() ?? new();
        _db.Execute("UPDATE Notes SET ImagePath = NULL, OriginalImagePath = NULL, AnnotationPath = NULL, UpdatedAt = @now WHERE Id = @id",
            ("@now", Now()), ("@id", id));
        return files;
    }

    /// <summary>
    /// Stores a drawing: <paramref name="drawnImagePath"/> becomes the displayed image, the untouched screenshot is kept as
    /// the original, and the strokes file makes the drawing editable later. Returns files that are no longer referenced.
    /// </summary>
    public List<string> SetDrawing(long id, string drawnImagePath, string annotationPath)
    {
        var note = Get(id) ?? throw new InvalidOperationException($"Note {id} not found.");
        string original = note.OriginalImagePath ?? note.ImagePath ?? throw new InvalidOperationException("Note has no image.");
        var obsolete = new List<string>();
        if (note.OriginalImagePath is not null && note.ImagePath is not null) obsolete.Add(note.ImagePath); // previous drawn copy
        if (note.AnnotationPath is not null) obsolete.Add(note.AnnotationPath);

        _db.Execute("""
            UPDATE Notes SET ImagePath = @image, OriginalImagePath = @original, AnnotationPath = @ink, UpdatedAt = @now WHERE Id = @id
            """, ("@image", drawnImagePath), ("@original", original), ("@ink", annotationPath), ("@now", Now()), ("@id", id));
        return obsolete;
    }

    /// <summary>Goes back to the untouched screenshot. Returns the drawn copy and strokes file for deletion.</summary>
    public List<string> RemoveDrawing(long id)
    {
        var note = Get(id);
        if (note?.OriginalImagePath is null) return new();
        var obsolete = new List<string>();
        if (note.ImagePath is not null) obsolete.Add(note.ImagePath);
        if (note.AnnotationPath is not null) obsolete.Add(note.AnnotationPath);
        _db.Execute("""
            UPDATE Notes SET ImagePath = OriginalImagePath, OriginalImagePath = NULL, AnnotationPath = NULL, UpdatedAt = @now WHERE Id = @id
            """, ("@now", Now()), ("@id", id));
        return obsolete;
    }

    /// <summary>Deletes the note. Returns the files it owned so they can be removed.</summary>
    public List<string> Delete(long id)
    {
        var files = Get(id)?.Files().ToList() ?? new();
        _db.Execute("DELETE FROM Notes WHERE Id = @id", ("@id", id));
        return files;
    }

    /// <summary>All files owned by the notes of a topic (for cleanup before the topic is deleted).</summary>
    public List<string> GetFilesOfTopic(long topicId) =>
        GetByTopic(topicId).SelectMany(n => n.Files()).ToList();

    // ───────── pinning ─────────

    public List<Note> GetPinned() =>
        _db.Query($"SELECT {Columns} FROM Notes n WHERE n.IsPinned = 1 ORDER BY n.Id", Map);

    public void SetPinned(long id, bool pinned) =>
        _db.Execute("UPDATE Notes SET IsPinned = @pinned WHERE Id = @id", ("@pinned", pinned ? 1 : 0), ("@id", id));

    public void SetPinPosition(long id, double x, double y) =>
        _db.Execute("UPDATE Notes SET PinX = @x, PinY = @y WHERE Id = @id", ("@x", x), ("@y", y), ("@id", id));

    public void SetPinWidth(long id, double width) =>
        _db.Execute("UPDATE Notes SET PinWidth = @w WHERE Id = @id", ("@w", width), ("@id", id));

    public int UnpinAll() => _db.Execute("UPDATE Notes SET IsPinned = 0 WHERE IsPinned = 1");

    // ───────── search ─────────

    /// <summary>Notes (across all topics) whose text contains every term of <paramref name="query"/>, case-insensitive; newest first.</summary>
    public List<NoteSearchResult> Search(string query, int limit = 100)
    {
        if (string.IsNullOrWhiteSpace(query)) return new();
        return _db.Query($"""
            SELECT {Columns}, t.Name FROM Notes n
            JOIN Topics t ON t.Id = n.TopicId
            WHERE qs_match(n.Text, @query)
            ORDER BY n.CreatedAt DESC, n.Id DESC
            LIMIT @limit
            """, r => new NoteSearchResult(Map(r), r.GetString(ColumnCount)), ("@query", query.Trim()), ("@limit", limit));
    }

    private static string Now() => Database.ToDb(DateTime.UtcNow);

    private static Note Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        TopicId = r.GetInt64(1),
        Text = r.GetString(2),
        ImagePath = r.IsDBNull(3) ? null : r.GetString(3),
        IsPinned = r.GetInt64(4) != 0,
        PinX = r.IsDBNull(5) ? null : r.GetDouble(5),
        PinY = r.IsDBNull(6) ? null : r.GetDouble(6),
        CreatedAt = Database.FromDb(r.GetString(7)),
        UpdatedAt = Database.FromDb(r.GetString(8)),
        OriginalImagePath = r.IsDBNull(9) ? null : r.GetString(9),
        AnnotationPath = r.IsDBNull(10) ? null : r.GetString(10),
        PinWidth = r.IsDBNull(11) ? null : r.GetDouble(11),
    };
}
