using System.Windows.Media.Imaging;

namespace QuickStash.Services;

/// <summary>
/// PNG files under &lt;data&gt;\images\. The database stores paths relative to the data folder
/// (e.g. "images\20250101-120000-ab12cd34.png") so the folder can be moved or backed up as a whole.
/// </summary>
internal sealed class ImageStore
{
    private const string ImagesFolderName = "images";
    private readonly string _dataFolder;
    private readonly string _imagesFolder;

    public ImageStore(string dataFolder)
    {
        _dataFolder = Path.GetFullPath(dataFolder);
        _imagesFolder = Path.Combine(_dataFolder, ImagesFolderName);
        Directory.CreateDirectory(_imagesFolder);
    }

    /// <summary>Encodes a frozen bitmap to PNG on a background thread. Returns the relative path to store in the database.</summary>
    public Task<string> SaveAsync(BitmapSource image)
    {
        if (!image.IsFrozen) image.Freeze();
        return Task.Run(() => Save(image));
    }

    public string Save(BitmapSource image)
    {
        string fileName = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}.png";
        string relative = Path.Combine(ImagesFolderName, fileName);
        string full = Path.Combine(_dataFolder, relative);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using (var stream = File.Create(full)) encoder.Save(stream);
        return relative;
    }

    /// <summary>Saves ink strokes (Ink Serialized Format) next to the images. Returns the relative path.</summary>
    public string SaveStrokes(System.Windows.Ink.StrokeCollection strokes)
    {
        string relative = Path.Combine(ImagesFolderName, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}.isf");
        using var stream = File.Create(Path.Combine(_dataFolder, relative));
        strokes.Save(stream, compress: true);
        return relative;
    }

    /// <summary>Loads saved ink strokes, or an empty collection if the file is missing or unreadable.</summary>
    public System.Windows.Ink.StrokeCollection LoadStrokes(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(Resolve(path))) return new();
        try
        {
            using var stream = File.OpenRead(Resolve(path));
            return new System.Windows.Ink.StrokeCollection(stream);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            Log.Error($"Could not load strokes {path}", ex);
            return new();
        }
    }

    /// <summary>Absolute path for a stored (relative or absolute) path.</summary>
    public string Resolve(string path) => Path.IsPathRooted(path) ? path : Path.Combine(_dataFolder, path);

    public bool Exists(string? path) => path is not null && File.Exists(Resolve(path));

    public void Delete(IEnumerable<string> paths)
    {
        foreach (var path in paths) Delete(path);
    }

    /// <summary>Deletes an image file. Only files inside the images folder are ever deleted.</summary>
    public void Delete(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        string full = Path.GetFullPath(Resolve(path));
        if (!full.StartsWith(_imagesFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            File.Delete(full);
        }
        catch (IOException ex) { Log.Error($"Could not delete {full}", ex); }
        catch (UnauthorizedAccessException ex) { Log.Error($"Could not delete {full}", ex); }
    }

    /// <summary>Pixel width from the file header only (no full decode); int.MaxValue if unknown.</summary>
    private static int GetPixelWidth(string fullPath)
    {
        try
        {
            using var stream = File.OpenRead(fullPath);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
            return decoder.Frames[0].PixelWidth;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or UnauthorizedAccessException)
        {
            return int.MaxValue;
        }
    }

    /// <summary>
    /// Loads an image, optionally downscaled while decoding (cheap thumbnails). Fully read into memory and frozen so the
    /// file is never locked and the bitmap can cross threads. Returns null if the file is missing or unreadable.
    /// </summary>
    public BitmapSource? Load(string? path, int decodePixelWidth = 0)
    {
        if (string.IsNullOrEmpty(path)) return null;
        string full = Resolve(path);
        if (!File.Exists(full)) return null;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.UriSource = new Uri(full, UriKind.Absolute);
            // Downscale while decoding (cheap thumbnails), but never upscale a small image.
            if (decodePixelWidth > 0 && decodePixelWidth < GetPixelWidth(full)) bitmap.DecodePixelWidth = decodePixelWidth;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or UnauthorizedAccessException)
        {
            Log.Error($"Could not load image {full}", ex);
            return null;
        }
    }
}
