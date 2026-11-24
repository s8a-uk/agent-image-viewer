using System.IO;
using AgentImageViewer.Core.Images;

namespace AgentImageViewer.Imaging;

/// <summary>
/// Decodes a thumbnail for every imported image, on the importing thread rather than the UI thread.
/// A file that does not decode is not an image as far as the viewer is concerned.
/// </summary>
public sealed class ThumbnailImageStore(IImageStore inner) : IImageStore
{
    // Thumbnails fill half the (resizable) window width; enough for ~400 DIP at 200% scaling.
    private const int ThumbnailWidth = 800;

    public CardImage? Import(string sourcePath)
    {
        if (inner.Import(sourcePath) is not { } image)
        {
            return null;
        }

        try
        {
            var thumbnail = BitmapLoader.Load(image.CachedPath, ThumbnailWidth);
            return new ThumbnailImage(image.SourcePath, image.CachedPath, image.Hash, thumbnail);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
            or ArgumentException or FileFormatException or OverflowException or OutOfMemoryException)
        {
            // FileFormatException (a damaged image) is not an IOException, so it is listed on its own.
            inner.Release([image]);
            return null;
        }
    }

    public void Release(IEnumerable<CardImage> images) => inner.Release(images);
}
