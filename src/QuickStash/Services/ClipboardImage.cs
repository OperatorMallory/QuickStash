using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QuickStash.Services;

/// <summary>Reads an image from the clipboard as a frozen, self-contained bitmap (detached from clipboard handles).</summary>
internal static class ClipboardImage
{
    public static BitmapSource? TryGet()
    {
        try
        {
            // Browsers and the Snipping Tool also put a "PNG" stream on the clipboard; it keeps transparency intact.
            if (Clipboard.ContainsData("PNG") && Clipboard.GetData("PNG") is MemoryStream png)
            {
                var decoder = new PngBitmapDecoder(png, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                return Copy(decoder.Frames[0]);
            }

            if (!Clipboard.ContainsImage()) return null;
            var image = Clipboard.GetImage();
            // CF_DIB data usually has a meaningless (zero) alpha channel; drop it so the image isn't fully transparent.
            return image is null ? null : Copy(new FormatConvertedBitmap(image, PixelFormats.Bgr32, null, 0));
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException or NotSupportedException or FileFormatException)
        {
            Log.Error("Could not read image from clipboard", ex);
            return null;
        }
    }

    /// <summary>Copies pixels into a new frozen bitmap so nothing references the clipboard afterwards.</summary>
    private static BitmapSource Copy(BitmapSource source)
    {
        int stride = (source.PixelWidth * source.Format.BitsPerPixel + 7) / 8;
        var pixels = new byte[stride * source.PixelHeight];
        source.CopyPixels(pixels, stride, 0);
        var copy = BitmapSource.Create(source.PixelWidth, source.PixelHeight, 96, 96, source.Format, source.Palette, pixels, stride);
        copy.Freeze();
        return copy;
    }
}
