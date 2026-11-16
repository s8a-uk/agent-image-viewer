using System.IO;
using System.Windows.Media.Imaging;

namespace AgentImageViewer.Imaging;

public static class BitmapLoader
{
    /// <summary>
    /// Most pixels a bitmap is decoded to (about 256 MB at 32 bits per pixel). Larger images are decoded smaller,
    /// so a small, highly compressed file cannot make the viewer allocate gigabytes.
    /// </summary>
    public const long MaxPixelCount = 64L * 1024 * 1024;

    /// <summary>
    /// Loads a frozen bitmap without keeping the file locked. A positive <paramref name="maxPixelWidth"/>
    /// decodes wider images down to that width; any image is decoded down to <see cref="MaxPixelCount"/> pixels.
    /// </summary>
    public static BitmapSource Load(string path, int maxPixelWidth = 0)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bitmap.UriSource = new Uri(path);
        var (width, height) = ReadPixelSize(path);
        var decodeWidth = DecodeWidth(width, height, maxPixelWidth);
        // Only shrink: decoding a small image at a larger width would just enlarge it.
        if (decodeWidth < width)
        {
            bitmap.DecodePixelWidth = decodeWidth;
        }

        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>The width to decode an image of the given size to, keeping its aspect ratio.</summary>
    public static int DecodeWidth(int width, int height, int maxPixelWidth = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var decodeWidth = maxPixelWidth > 0 ? Math.Min(width, maxPixelWidth) : width;
        // The decoded height scales with the width: w' * (h * w' / w) <= MaxPixelCount.
        var widthForPixelCount = (int)Math.Clamp(Math.Floor(Math.Sqrt(MaxPixelCount * (double)width / height)), 1, int.MaxValue);
        return Math.Min(decodeWidth, widthForPixelCount);
    }

    /// <summary>The image's size in pixels, read from its header without decoding the pixels.</summary>
    public static (int Width, int Height) ReadPixelSize(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
        var frame = decoder.Frames[0];
        return (frame.PixelWidth, frame.PixelHeight);
    }
}
