namespace QuickStash.Services;

/// <summary>Locations of everything QuickStash writes: %AppData%\QuickStash\{quickstash.db, images\, settings.json, quickstash.log}.</summary>
internal static class AppPaths
{
    /// <summary>%AppData%\QuickStash, or the folder in the QUICKSTASH_DATA environment variable (testing / portable setups).</summary>
    public static string DataFolder { get; } =
        Environment.GetEnvironmentVariable("QUICKSTASH_DATA") is { Length: > 0 } custom
            ? Path.GetFullPath(custom)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickStash");

    public static string DatabasePath => Path.Combine(DataFolder, "quickstash.db");
    public static string ImagesFolder => Path.Combine(DataFolder, "images");
    public static string SettingsPath => Path.Combine(DataFolder, "settings.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataFolder);
        Directory.CreateDirectory(ImagesFolder);
    }
}
