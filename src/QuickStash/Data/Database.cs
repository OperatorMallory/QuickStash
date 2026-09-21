using System.Globalization;
using Microsoft.Data.Sqlite;

namespace QuickStash.Data;

/// <summary>
/// Owns the single SQLite connection (all access happens on the UI thread; queries take microseconds at this scale).
/// Schema is versioned with PRAGMA user_version; <see cref="Migrate"/> upgrades step by step.
/// </summary>
internal sealed class Database : IDisposable
{
    public const int SchemaVersion = 1;

    public SqliteConnection Connection { get; }

    public Database(string path)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = false,
        };
        Connection = new SqliteConnection(builder.ToString());
        Connection.Open();
        Execute("PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;");
        RegisterFunctions();
        Migrate();
    }

    private void Migrate()
    {
        long version = Scalar<long>("PRAGMA user_version;");
        if (version >= SchemaVersion) return;

        using var tx = Connection.BeginTransaction();
        if (version < 1)
        {
            Execute("""
                CREATE TABLE Topics (
                    Id       INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name     TEXT NOT NULL COLLATE NOCASE,
                    LastUsed TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IX_Topics_Name ON Topics(Name);
                CREATE INDEX IX_Topics_LastUsed ON Topics(LastUsed DESC);

                CREATE TABLE TopicProcesses (
                    TopicId     INTEGER NOT NULL REFERENCES Topics(Id) ON DELETE CASCADE,
                    ProcessName TEXT NOT NULL COLLATE NOCASE,
                    PRIMARY KEY (TopicId, ProcessName)
                );
                CREATE INDEX IX_TopicProcesses_ProcessName ON TopicProcesses(ProcessName);

                CREATE TABLE Notes (
                    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
                    TopicId   INTEGER NOT NULL REFERENCES Topics(Id) ON DELETE CASCADE,
                    Text      TEXT NOT NULL DEFAULT '',
                    ImagePath TEXT NULL,
                    IsPinned  INTEGER NOT NULL DEFAULT 0,
                    PinX      REAL NULL,
                    PinY      REAL NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                CREATE INDEX IX_Notes_Topic_Created ON Notes(TopicId, CreatedAt DESC);
                CREATE INDEX IX_Notes_Pinned ON Notes(IsPinned) WHERE IsPinned = 1;
                """, tx);
        }
        Execute($"PRAGMA user_version = {SchemaVersion};", tx);
        tx.Commit();
    }

    /// <summary>
    /// qs_match(text, query): true when every whitespace-separated term of the query occurs in text, ignoring case
    /// (full Unicode, unlike SQLite's ASCII-only LIKE). No escaping needed because the query is never parsed as a pattern.
    /// </summary>
    private void RegisterFunctions()
    {
        Connection.CreateFunction("qs_match", (string? text, string? query) => Matches(text, query), isDeterministic: true);
    }

    internal static bool Matches(string? text, string? query)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(query)) return false;
        foreach (var term in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (text.IndexOf(term, StringComparison.CurrentCultureIgnoreCase) < 0) return false;
        }
        return true;
    }

    // ───────── small helpers so repositories stay readable ─────────

    public SqliteCommand Command(string sql, SqliteTransaction? tx = null, params (string Name, object? Value)[] parameters)
    {
        var cmd = Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    public int Execute(string sql, SqliteTransaction? tx = null, params (string, object?)[] parameters)
    {
        using var cmd = Command(sql, tx, parameters);
        return cmd.ExecuteNonQuery();
    }

    public int Execute(string sql, params (string, object?)[] parameters) => Execute(sql, null, parameters);

    public T Scalar<T>(string sql, params (string, object?)[] parameters)
    {
        using var cmd = Command(sql, null, parameters);
        var result = cmd.ExecuteScalar();
        return result is null or DBNull ? default! : (T)Convert.ChangeType(result, typeof(T), CultureInfo.InvariantCulture);
    }

    public List<T> Query<T>(string sql, Func<SqliteDataReader, T> map, params (string, object?)[] parameters)
    {
        using var cmd = Command(sql, null, parameters);
        using var reader = cmd.ExecuteReader();
        var list = new List<T>();
        while (reader.Read()) list.Add(map(reader));
        return list;
    }

    public static string ToDb(DateTime utc) => utc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    public static DateTime FromDb(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();

    public void Dispose() => Connection.Dispose();
}
