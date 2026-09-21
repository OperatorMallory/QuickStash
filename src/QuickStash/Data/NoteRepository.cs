using Microsoft.Data.Sqlite;
using QuickStash.Models;

namespace QuickStash.Data;

internal sealed class NoteRepository
{
    private const string Columns = "n.Id, n.TopicId, n.Text, n.ImagePath, n.IsPinned, n.PinX, n.PinY, n.CreatedAt, n.UpdatedAt";

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
            ("@text", text), ("@now", Database.ToDb(DateTime.UtcNow)), ("@id", id));

    /// <summary>Replaces (or clears) the note's image. Returns the previous path so its file can be deleted.</summary>
    public string? SetImage(long id, string? imagePath)
    {
        string? previous = _db.Scalar<string?>("SELECT ImagePath FROM Notes WHERE Id = @id", ("@id", id));
        _db.Execute("UPDATE Notes SET ImagePath = @image, UpdatedAt = @now WHERE Id = @id",
            ("@image", imagePath), ("@now", Database.ToDb(DateTime.UtcNow)), ("@id", id));
        return previous;
    }

    /// <summary>Deletes the note. Returns its image path (if any) so the file can be removed.</summary>
    public string? Delete(long id)
    {
        string? image = _db.Scalar<string?>("SELECT ImagePath FROM Notes WHERE Id = @id", ("@id", id));
        _db.Execute("DELETE FROM Notes WHERE Id = @id", ("@id", id));
        return image;
    }

    // ───────── pinning ─────────

    public List<Note> GetPinned() =>
        _db.Query($"SELECT {Columns} FROM Notes n WHERE n.IsPinned = 1 ORDER BY n.Id", Map);

    public void SetPinned(long id, bool pinned) =>
        _db.Execute("UPDATE Notes SET IsPinned = @pinned WHERE Id = @id", ("@pinned", pinned ? 1 : 0), ("@id", id));

    public void SetPinPosition(long id, double x, double y) =>
        _db.Execute("UPDATE Notes SET PinX = @x, PinY = @y WHERE Id = @id", ("@x", x), ("@y", y), ("@id", id));

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
            """, r => new NoteSearchResult(Map(r), r.GetString(9)), ("@query", query.Trim()), ("@limit", limit));
    }

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
    };
}
