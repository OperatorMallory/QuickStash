using System.Diagnostics;

namespace QuickStash.Services;

/// <summary>Tiny append-only log in the data folder. Useful for bug reports; trimmed at startup so it never grows large.</summary>
internal static class Log
{
    private static readonly object Gate = new();
    private static string? _path;

    public static void Initialize(string folder)
    {
        _path = Path.Combine(folder, "quickstash.log");
        try
        {
            var info = new FileInfo(_path);
            if (info.Exists && info.Length > 256 * 1024) info.Delete();
        }
        catch (IOException) { }
    }

    public static void Info(string message) => Write("INFO ", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}";
        Debug.WriteLine(line);
        if (_path is null) return;
        lock (Gate)
        {
            try { File.AppendAllText(_path, line + Environment.NewLine); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
