using System.Security.Cryptography;

namespace AgentImageViewer.Core.Images;

/// <summary>
/// Copies incoming images into a Windows-side folder, because sources under WSL's /tmp
/// (or files the hook cleans up) may disappear before the user looks at them.
/// </summary>
public sealed class ImageCache : IImageStore
{
    /// <summary>Largest file copied; screenshots are a few megabytes.</summary>
    public const long MaxFileBytes = 100L * 1024 * 1024;

    // UNC paths the viewer reads from: the WSL file system, where the hook's files live.
    // Any other UNC path would make Windows connect, and authenticate, to that host.
    private static readonly string[] AllowedUncPrefixes = [@"\\wsl.localhost\", @"\\wsl$\"];

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff",
    };

    private readonly string _directory;
    private volatile bool _closed;

    /// <summary>Takes over <paramref name="directory"/>: everything already in it is deleted.</summary>
    public ImageCache(string directory)
    {
        _directory = directory;

        // Cards are not persisted, so anything left over from a previous run is garbage.
        Clear();
    }

    /// <summary>
    /// Whether the viewer reads from <paramref name="path"/>: a fully qualified local path, or a path on the WSL file
    /// system. Other UNC paths, device paths (<c>\\.\</c>, <c>\\?\</c>) and relative paths are refused.
    /// </summary>
    public static bool IsAllowedSource(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var normalized = path.Replace('/', '\\');
        if (normalized.StartsWith(@"\\", StringComparison.Ordinal))
        {
            // "." and ".." segments could step from an allowed prefix to another host or share.
            return AllowedUncPrefixes.Any(prefix => normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                && !normalized.Split('\\').Any(segment => segment is "." or "..");
        }

        return Path.IsPathFullyQualified(path);
    }

    /// <summary>Copies the file. Returns null if it is not an allowed, readable image file of a sensible size.</summary>
    public CardImage? Import(string sourcePath)
    {
        var extension = Path.GetExtension(sourcePath);
        if (_closed || !SupportedExtensions.Contains(extension) || !IsAllowedSource(sourcePath))
        {
            return null;
        }

        var cachedPath = Path.Combine(_directory, $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}");
        try
        {
            if (new FileInfo(sourcePath).Length > MaxFileBytes)
            {
                return null;
            }

            File.Copy(sourcePath, cachedPath);
            // File.Copy keeps a read-only attribute, which would make the copy impossible to delete.
            File.SetAttributes(cachedPath, FileAttributes.Normal);
            string hash;
            using (var stream = File.OpenRead(cachedPath))
            {
                hash = Convert.ToHexString(SHA256.HashData(stream));
            }

            if (_closed)
            {
                // Closed while copying: the cache has already been emptied for good.
                Delete(cachedPath);
                return null;
            }

            return new CardImage(sourcePath, cachedPath, hash);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            Delete(cachedPath);
            return null;
        }
    }

    /// <summary>Empties the cache for good: later imports are refused. Called when the viewer exits.</summary>
    public void Close()
    {
        _closed = true;
        Clear();
    }

    /// <summary>Deletes every cached file.</summary>
    public void Clear()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                // Earlier versions kept the read-only attribute of copied files, which blocks the delete.
                foreach (var file in Directory.EnumerateFiles(_directory))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file that is still locked stays behind; the next start tries again.
        }

        Directory.CreateDirectory(_directory);
    }

    public void Release(IEnumerable<CardImage> images)
    {
        foreach (var image in images)
        {
            Delete(image.CachedPath);
        }
    }

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the cleanup on the next start.
        }
    }
}
