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

    /// <summary>Absolute path for a stored (relative or absolute) path.</summary>
    public string Resolve(string path) => Path.IsPathRooted(path) ? path : Path.Combine(_dataFolder, path);

    public bool Exists(string? path) => path is not null && File.Exists(Resolve(path));

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
            if (decodePixelWidth > 0) bitmap.DecodePixelWidth = decodePixelWidth;
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
