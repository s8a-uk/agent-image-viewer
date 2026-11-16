using System.IO;
using AgentImageViewer.Core.Images;
using AgentImageViewer.Imaging;
using AgentImageViewer.Tests.TestSupport;

namespace AgentImageViewer.Tests.Imaging;

public sealed class ThumbnailImageStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _cacheDirectory;
    private readonly ThumbnailImageStore _store;

    public ThumbnailImageStoreTests()
    {
        _cacheDirectory = _temp.File("cache");
        _store = new ThumbnailImageStore(new ImageCache(_cacheDirectory));
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Import_returns_the_cached_image_with_a_frozen_thumbnail()
    {
        var source = TestImages.WritePng(_temp.File("shot.png"), 320, 200);

        var image = Assert.IsType<ThumbnailImage>(_store.Import(source));

        Assert.Equal(source, image.SourcePath);
        Assert.Equal("shot.png", image.FileName);
        Assert.Equal(_cacheDirectory, Path.GetDirectoryName(image.CachedPath));
        Assert.True(File.Exists(image.CachedPath));
        Assert.Equal(64, image.Hash.Length);
        Assert.True(image.Thumbnail.IsFrozen);
        Assert.Equal(320, image.Thumbnail.PixelWidth);
        Assert.Equal(200, image.Thumbnail.PixelHeight);
    }

    [Fact]
    public void A_large_image_gets_a_reduced_thumbnail_but_its_copy_stays_intact()
    {
        var source = TestImages.WritePng(_temp.File("large.png"), 2400, 600);

        var image = Assert.IsType<ThumbnailImage>(_store.Import(source));

        Assert.Equal(800, image.Thumbnail.PixelWidth);
        Assert.Equal(200, image.Thumbnail.PixelHeight);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(image.CachedPath));
    }

    [Fact]
    public void A_file_that_is_not_really_an_image_is_rejected_and_its_copy_removed()
    {
        var source = _temp.File("fake.png");
        File.WriteAllText(source, "This is text, whatever the extension says.");

        Assert.Null(_store.Import(source));

        Assert.Empty(Directory.GetFiles(_cacheDirectory));
    }

    [Fact]
    public void A_file_without_an_image_extension_is_rejected()
    {
        var source = _temp.File("notes.txt");
        File.WriteAllText(source, "text");

        Assert.Null(_store.Import(source));

        Assert.Empty(Directory.GetFiles(_cacheDirectory));
    }

    [Fact]
    public void The_same_picture_imported_twice_has_the_same_hash()
    {
        var first = _store.Import(TestImages.WritePng(_temp.File("one.png"), 64, 48));
        var second = _store.Import(TestImages.WritePng(_temp.File("two.png"), 64, 48));
        var different = _store.Import(TestImages.WritePng(_temp.File("three.png"), 64, 48, shade: 0x20));

        Assert.Equal(first!.Hash, second!.Hash);
        Assert.NotEqual(first.Hash, different!.Hash);
    }

    [Fact]
    public void Release_deletes_the_cached_copy()
    {
        var image = _store.Import(TestImages.WritePng(_temp.File("shot.png"), 64, 48))!;

        _store.Release([image]);

        Assert.False(File.Exists(image.CachedPath));
    }
}
