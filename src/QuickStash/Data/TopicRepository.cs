using Microsoft.Data.Sqlite;
using QuickStash.Models;

namespace QuickStash.Data;

internal sealed class TopicRepository
{
    private readonly Database _db;

    public TopicRepository(Database db) => _db = db;

    /// <summary>All topics, most recently used first, with their linked process names.</summary>
    public List<Topic> GetAll()
    {
        var topics = _db.Query("SELECT Id, Name, LastUsed FROM Topics ORDER BY LastUsed DESC, Id DESC", Map);
        var byId = topics.ToDictionary(t => t.Id);
        foreach (var (topicId, process) in _db.Query("SELECT TopicId, ProcessName FROM TopicProcesses ORDER BY ProcessName",
                     r => (r.GetInt64(0), r.GetString(1))))
        {
            if (byId.TryGetValue(topicId, out var topic)) topic.ProcessNames.Add(process);
        }
        return topics;
    }

    public Topic? Get(long id) => Load("SELECT Id, Name, LastUsed FROM Topics WHERE Id = @id", ("@id", id));

    public Topic? FindByName(string name) =>
        Load("SELECT Id, Name, LastUsed FROM Topics WHERE Name = @name", ("@name", name.Trim()));

    /// <summary>The topic linked to <paramref name="processName"/>; if several are, the most recently used one.</summary>
    public Topic? FindByProcess(string processName) =>
        Load("""
             SELECT t.Id, t.Name, t.LastUsed FROM Topics t
             JOIN TopicProcesses p ON p.TopicId = t.Id
             WHERE p.ProcessName = @process
             ORDER BY t.LastUsed DESC LIMIT 1
             """, ("@process", processName));

    public Topic Create(string name, string? linkProcess = null)
    {
        name = name.Trim();
        if (name.Length == 0) throw new ArgumentException("Topic name is required.", nameof(name));

        using var tx = _db.Connection.BeginTransaction();
        var now = DateTime.UtcNow;
        long id;
        using (var cmd = _db.Command("INSERT INTO Topics (Name, LastUsed) VALUES (@name, @now); SELECT last_insert_rowid();", tx,
                   ("@name", name), ("@now", Database.ToDb(now))))
        {
            id = (long)cmd.ExecuteScalar()!;
        }
        if (!string.IsNullOrWhiteSpace(linkProcess)) LinkProcess(id, linkProcess, tx);
        tx.Commit();
        return Get(id)!;
    }

    public void Rename(long id, string name)
    {
        name = name.Trim();
        if (name.Length == 0) throw new ArgumentException("Topic name is required.", nameof(name));
        _db.Execute("UPDATE Topics SET Name = @name WHERE Id = @id", ("@name", name), ("@id", id));
    }

    /// <summary>Deletes the topic and (by cascade) its notes. Returns the files those notes owned so they can be removed.</summary>
    public List<string> Delete(long id)
    {
        var files = _db.Query("SELECT ImagePath, OriginalImagePath, AnnotationPath FROM Notes WHERE TopicId = @id",
                r => new[] { r.IsDBNull(0) ? null : r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2) },
                ("@id", id))
            .SelectMany(paths => paths)
            .OfType<string>()
            .ToList();
        _db.Execute("DELETE FROM Topics WHERE Id = @id", ("@id", id));
        return files;
    }

    /// <summary>The topic linked to the process, creating (and linking) one named after the executable if there is none.</summary>
    public Topic GetOrCreateForProcess(string processName)
    {
        if (FindByProcess(processName) is { } linked) return linked;
        string name = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? processName[..^4] : processName;
        if (FindByName(name) is { } existing)
        {
            LinkProcess(existing.Id, processName);
            return Get(existing.Id)!;
        }
        return Create(name, processName);
    }

    /// <summary>The catch-all topic for captures made when no game was in front.</summary>
    public Topic GetOrCreateInbox() => FindByName(InboxName) ?? Create(InboxName);

    public const string InboxName = "Inbox";

    /// <summary>Marks the topic as just used so it moves to the top of the recent list.</summary>
    public void Touch(long id) =>
        _db.Execute("UPDATE Topics SET LastUsed = @now WHERE Id = @id", ("@now", Database.ToDb(DateTime.UtcNow)), ("@id", id));

    public void LinkProcess(long id, string processName, SqliteTransaction? tx = null) =>
        _db.Execute("INSERT OR IGNORE INTO TopicProcesses (TopicId, ProcessName) VALUES (@id, @process)", tx,
            ("@id", id), ("@process", processName.Trim().ToLowerInvariant()));

    public void UnlinkProcess(long id, string processName) =>
        _db.Execute("DELETE FROM TopicProcesses WHERE TopicId = @id AND ProcessName = @process",
            ("@id", id), ("@process", processName));

    public List<string> GetProcesses(long id) =>
        _db.Query("SELECT ProcessName FROM TopicProcesses WHERE TopicId = @id ORDER BY ProcessName", r => r.GetString(0), ("@id", id));

    private Topic? Load(string sql, params (string, object?)[] parameters)
    {
        var topic = _db.Query(sql, Map, parameters).FirstOrDefault();
        topic?.ProcessNames.AddRange(GetProcesses(topic.Id));
        return topic;
    }

    private static Topic Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        Name = r.GetString(1),
        LastUsed = Database.FromDb(r.GetString(2)),
    };
}
