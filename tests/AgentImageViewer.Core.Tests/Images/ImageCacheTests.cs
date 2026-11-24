using System.Security.Cryptography;
using AgentImageViewer.Core.Images;
using AgentImageViewer.Core.Tests.TestSupport;

namespace AgentImageViewer.Core.Tests.Images;

public sealed class ImageCacheTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _cacheDirectory;

    public ImageCacheTests()
    {
        _cacheDirectory = Path.Combine(_temp.Path, "AgentImageViewer", "cache");
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Creating_the_cache_creates_its_directory()
    {
        _ = new ImageCache(_cacheDirectory);

        Assert.True(Directory.Exists(_cacheDirectory));
    }

    [Fact]
    public void Creating_the_cache_deletes_what_a_previous_run_left_behind()
    {
        Directory.CreateDirectory(Path.Combine(_cacheDirectory, "nested"));
        File.WriteAllText(Path.Combine(_cacheDirectory, "old.png"), "old");
        File.WriteAllText(Path.Combine(_cacheDirectory, "nested", "older.png"), "older");

        _ = new ImageCache(_cacheDirectory);

        Assert.True(Directory.Exists(_cacheDirectory));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_cacheDirectory));
    }

    [Fact]
    public void Import_copies_the_file_into_the_cache_and_hashes_its_content()
    {
        var cache = new ImageCache(_cacheDirectory);
        var source = _temp.WriteFile("Screenshot 1.png", "pixels");

        var image = cache.Import(source);

        Assert.NotNull(image);
        Assert.Equal(source, image.SourcePath);
        Assert.Equal("Screenshot 1.png", image.FileName);
        Assert.Equal(_cacheDirectory, Path.GetDirectoryName(image.CachedPath));
        Assert.NotEqual("Screenshot 1.png", Path.GetFileName(image.CachedPath));
        Assert.Equal("pixels", File.ReadAllText(image.CachedPath));
        Assert.Equal(Convert.ToHexString(SHA256.HashData("pixels"u8)), image.Hash);
    }

    [Fact]
    public void The_copy_outlives_the_source()
    {
        var cache = new ImageCache(_cacheDirectory);
        var source = _temp.WriteFile("one.png", "pixels");

        var image = cache.Import(source);
        File.Delete(source);

        Assert.Equal("pixels", File.ReadAllText(image!.CachedPath));
    }

    [Fact]
    public void Importing_the_same_content_twice_gives_equal_hashes_but_separate_copies()
    {
        var cache = new ImageCache(_cacheDirectory);
        var first = cache.Import(_temp.WriteFile("one.png", "same"));
        var second = cache.Import(_temp.WriteFile("two.png", "same"));

        Assert.Equal(first!.Hash, second!.Hash);
        Assert.NotEqual(first.CachedPath, second.CachedPath);
        Assert.Equal(2, Directory.GetFiles(_cacheDirectory).Length);
    }

    [Fact]
    public void A_file_rewritten_at_the_same_path_gets_a_new_hash()
    {
        var cache = new ImageCache(_cacheDirectory);
        var source = _temp.WriteFile("shot.png", "before");
        var before = cache.Import(source);
        File.WriteAllText(source, "after");

        var after = cache.Import(source);

        Assert.NotEqual(before!.Hash, after!.Hash);
        Assert.Equal("before", File.ReadAllText(before.CachedPath));
    }

    [Theory]
    [InlineData(".png", ".png")]
    [InlineData(".jpg", ".jpg")]
    [InlineData(".jpeg", ".jpeg")]
    [InlineData(".gif", ".gif")]
    [InlineData(".bmp", ".bmp")]
    [InlineData(".webp", ".webp")]
    [InlineData(".tif", ".tif")]
    [InlineData(".tiff", ".tiff")]
    [InlineData(".PNG", ".png")]
    [InlineData(".Jpg", ".jpg")]
    public void Image_extensions_are_accepted_in_any_case_and_cached_in_lower_case(string extension, string cachedExtension)
    {
        var cache = new ImageCache(_cacheDirectory);

        var image = cache.Import(_temp.WriteFile($"image{extension}", "pixels"));

        Assert.NotNull(image);
        Assert.Equal(cachedExtension, Path.GetExtension(image.CachedPath));
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("vector.svg")]
    [InlineData("document.pdf")]
    [InlineData("png")]
    [InlineData("archive.png.zip")]
    public void Other_files_are_not_imported(string name)
    {
        var cache = new ImageCache(_cacheDirectory);

        Assert.Null(cache.Import(_temp.WriteFile(name, "content")));

        Assert.Empty(Directory.EnumerateFileSystemEntries(_cacheDirectory));
    }

    [Fact]
    public void A_missing_source_is_not_imported_and_leaves_nothing_behind()
    {
        var cache = new ImageCache(_cacheDirectory);

        Assert.Null(cache.Import(_temp.File("missing.png")));

        Assert.Empty(Directory.EnumerateFileSystemEntries(_cacheDirectory));
    }

    [Fact]
    public void Release_deletes_only_the_given_copies()
    {
        var cache = new ImageCache(_cacheDirectory);
        var kept = cache.Import(_temp.WriteFile("kept.png", "kept"))!;
        var first = cache.Import(_temp.WriteFile("first.png", "first"))!;
        var second = cache.Import(_temp.WriteFile("second.png", "second"))!;

        cache.Release([first, second]);

        Assert.Equal([kept.CachedPath], Directory.GetFiles(_cacheDirectory));
        Assert.True(File.Exists(first.SourcePath));
    }

    [Fact]
    public void Releasing_an_image_twice_is_harmless()
    {
        var cache = new ImageCache(_cacheDirectory);
        var image = cache.Import(_temp.WriteFile("one.png", "pixels"))!;

        cache.Release([image]);
        cache.Release([image]);

        Assert.False(File.Exists(image.CachedPath));
    }

    [Theory]
    [InlineData(@"C:\Users\me\Pictures\shot.png", true)]
    [InlineData("C:/Users/me/Pictures/shot.png", true)]
    [InlineData(@"\\wsl.localhost\Ubuntu\tmp\shot.png", true)]
    [InlineData(@"\\WSL.LOCALHOST\Ubuntu\tmp\shot.png", true)]
    [InlineData(@"\\wsl$\Ubuntu\tmp\shot.png", true)]
    [InlineData("//wsl.localhost/Ubuntu/tmp/shot.png", true)]
    [InlineData(@"\\attacker\share\shot.png", false)]
    [InlineData("//attacker/share/shot.png", false)]
    [InlineData(@"\\wsl.localhost.attacker.example\share\shot.png", false)]
    [InlineData(@"\\wsl.localhost\..\attacker\share\shot.png", false)]
    [InlineData(@"\\wsl.localhost\Ubuntu\.\shot.png", false)]
    [InlineData(@"\\?\C:\Users\me\shot.png", false)]
    [InlineData(@"\\.\C:\Users\me\shot.png", false)]
    [InlineData(@"\\?\UNC\attacker\share\shot.png", false)]
    [InlineData("shot.png", false)]
    [InlineData(@"Pictures\shot.png", false)]
    public void Only_local_and_WSL_paths_are_allowed_sources(string path, bool expected)
    {
        if (!OperatingSystem.IsWindows() && path.Length > 1 && path[1] == ':')
        {
            // A drive letter only means "fully qualified" on Windows.
            expected = false;
        }

        Assert.Equal(expected, ImageCache.IsAllowedSource(path));
    }

    [Fact]
    public void A_path_on_another_host_is_not_imported_and_leaves_nothing_behind()
    {
        var cache = new ImageCache(_cacheDirectory);

        Assert.Null(cache.Import(@"\\attacker\share\shot.png"));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_cacheDirectory));
    }

    [Fact]
    public void A_file_larger_than_the_limit_is_not_imported_and_leaves_nothing_behind()
    {
        var cache = new ImageCache(_cacheDirectory);
        var source = Path.Combine(_temp.Path, "huge.png");
        using (var stream = File.Create(source))
        {
            stream.SetLength(ImageCache.MaxFileBytes + 1);
        }

        Assert.Null(cache.Import(source));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_cacheDirectory));
    }

    [Fact]
    public void The_copy_of_a_read_only_file_is_writable_and_can_be_released()
    {
        var cache = new ImageCache(_cacheDirectory);
        var source = _temp.WriteFile("locked.png", "pixels");
        File.SetAttributes(source, FileAttributes.ReadOnly);

        try
        {
            var image = cache.Import(source)!;

            Assert.False(File.GetAttributes(image.CachedPath).HasFlag(FileAttributes.ReadOnly));
            cache.Release([image]);
            Assert.False(File.Exists(image.CachedPath));
        }
        finally
        {
            File.SetAttributes(source, FileAttributes.Normal);
        }
    }

    [Fact]
    public void Clear_deletes_every_copy_including_read_only_leftovers()
    {
        var cache = new ImageCache(_cacheDirectory);
        var image = cache.Import(_temp.WriteFile("one.png", "pixels"))!;
        var leftover = Path.Combine(_cacheDirectory, "leftover.png");
        File.WriteAllText(leftover, "old");
        File.SetAttributes(leftover, FileAttributes.ReadOnly);

        cache.Clear();

        Assert.True(Directory.Exists(_cacheDirectory));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_cacheDirectory));
        Assert.False(File.Exists(image.CachedPath));
    }

    [Fact]
    public void After_Close_the_cache_is_empty_and_refuses_imports()
    {
        var cache = new ImageCache(_cacheDirectory);
        _ = cache.Import(_temp.WriteFile("one.png", "pixels"));

        cache.Close();

        Assert.Null(cache.Import(_temp.WriteFile("two.png", "pixels")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_cacheDirectory));
    }
}
