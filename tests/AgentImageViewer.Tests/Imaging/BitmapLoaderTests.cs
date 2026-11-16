using System.IO;
using AgentImageViewer.Imaging;
using AgentImageViewer.Tests.TestSupport;

namespace AgentImageViewer.Tests.Imaging;

public sealed class BitmapLoaderTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Load_decodes_the_image_at_its_own_size_by_default()
    {
        var path = TestImages.WritePng(_temp.File("wide.png"), 1200, 300);

        var bitmap = BitmapLoader.Load(path);

        Assert.Equal(1200, bitmap.PixelWidth);
        Assert.Equal(300, bitmap.PixelHeight);
    }

    [Fact]
    public void Load_shrinks_an_image_wider_than_the_limit_and_keeps_its_proportions()
    {
        var path = TestImages.WritePng(_temp.File("wide.png"), 1200, 300);

        var bitmap = BitmapLoader.Load(path, maxPixelWidth: 400);

        Assert.Equal(400, bitmap.PixelWidth);
        Assert.Equal(100, bitmap.PixelHeight);
    }

    [Fact]
    public void Load_never_enlarges_an_image_narrower_than_the_limit()
    {
        var path = TestImages.WritePng(_temp.File("small.png"), 64, 48);

        var bitmap = BitmapLoader.Load(path, maxPixelWidth: 400);

        Assert.Equal(64, bitmap.PixelWidth);
        Assert.Equal(48, bitmap.PixelHeight);
    }

    [Fact]
    public void The_bitmap_is_frozen_so_any_thread_can_show_it()
    {
        var path = TestImages.WritePng(_temp.File("image.png"), 64, 48);

        Assert.True(BitmapLoader.Load(path).IsFrozen);
        Assert.True(BitmapLoader.Load(path, maxPixelWidth: 32).IsFrozen);
    }

    [Fact]
    public void The_file_is_not_kept_open()
    {
        var path = TestImages.WritePng(_temp.File("image.png"), 64, 48);

        var bitmap = BitmapLoader.Load(path, maxPixelWidth: 32);
        File.Delete(path);

        Assert.False(File.Exists(path));
        Assert.Equal(32, bitmap.PixelWidth);
    }

    [Theory]
    [InlineData(1920, 1080, 0, 1920)]
    [InlineData(1920, 1080, 800, 800)]
    [InlineData(400, 300, 800, 400)]
    [InlineData(8192, 8192, 0, 8192)]
    [InlineData(30000, 30000, 0, 8192)]
    [InlineData(30000, 30000, 800, 800)]
    [InlineData(100000, 10, 0, 100000)]
    [InlineData(1, 100000000, 0, 1)]
    public void DecodeWidth_shrinks_only_past_the_requested_width_or_the_pixel_budget(int width, int height, int maxPixelWidth, int expected)
    {
        Assert.Equal(expected, BitmapLoader.DecodeWidth(width, height, maxPixelWidth));
    }

    [Theory]
    [InlineData(30000, 30000)]
    [InlineData(20000, 50000)]
    [InlineData(65535, 65535)]
    public void A_huge_image_is_decoded_within_the_pixel_budget(int width, int height)
    {
        var decodeWidth = BitmapLoader.DecodeWidth(width, height);
        var decodeHeight = (long)Math.Ceiling(height * (double)decodeWidth / width);

        Assert.True(decodeWidth * decodeHeight <= BitmapLoader.MaxPixelCount + decodeWidth);
    }
}
