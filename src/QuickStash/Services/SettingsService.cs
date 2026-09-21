using System.Text.Json;
using QuickStash.Models;

namespace QuickStash.Services;

/// <summary>Loads and saves <see cref="AppSettings"/> as settings.json. A missing or corrupt file yields defaults.</summary>
internal sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep "Ctrl+Shift+Space" readable
    };
    private readonly string _path;

    public SettingsService(string path)
    {
        _path = path;
        Current = Load();
    }

    public AppSettings Current { get; private set; }

    /// <summary>Raised after settings were saved, with the new values.</summary>
    public event EventHandler<AppSettings>? Changed;

    private AppSettings Load()
    {
        AppSettings settings;
        try
        {
            settings = File.Exists(_path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Options) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Error("settings.json could not be read; using defaults", ex);
            settings = new AppSettings();
        }
        settings.Normalize();
        return settings;
    }

    public void Save(AppSettings settings)
    {
        settings.Normalize();
        Current = settings.Clone();
        try
        {
            // Write to a temp file first so a crash never leaves a half-written settings file.
            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Current, Options));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Could not save settings", ex);
        }
        Changed?.Invoke(this, Current);
    }
}
