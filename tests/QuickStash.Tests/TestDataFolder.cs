using Microsoft.Data.Sqlite;
using QuickStash.Data;

namespace QuickStash.Tests;

/// <summary>A throwaway data folder with a fresh database, deleted after the test.</summary>
internal sealed class TestDataFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "QuickStashTests", Guid.NewGuid().ToString("N"));
    public Database Db { get; }
    public TopicRepository Topics { get; }
    public NoteRepository Notes { get; }

    public TestDataFolder()
    {
        Directory.CreateDirectory(Path);
        Db = new Database(System.IO.Path.Combine(Path, "quickstash.db"));
        Topics = new TopicRepository(Db);
        Notes = new NoteRepository(Db);
    }

    public void Dispose()
    {
        Db.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
    }
}
