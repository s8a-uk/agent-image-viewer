using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AgentImageViewer.Tests.TestSupport;

public static class TestImages
{
    /// <summary>Writes a PNG of the given size; <paramref name="shade"/> makes its content (and hash) distinct.</summary>
    public static string WritePng(string path, int width, int height, byte shade = 0x80)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];
        Array.Fill(pixels, shade);
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }
}
