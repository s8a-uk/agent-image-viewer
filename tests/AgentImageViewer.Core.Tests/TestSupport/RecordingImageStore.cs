using AgentImageViewer.Core.Images;

namespace AgentImageViewer.Core.Tests.TestSupport;

/// <summary>An image store that touches no files and remembers what was released.</summary>
public sealed class RecordingImageStore : IImageStore
{
    public List<CardImage> Released { get; } = [];

    public CardImage? Import(string sourcePath) => Image(sourcePath);

    public void Release(IEnumerable<CardImage> images) => Released.AddRange(images);

    /// <summary>An image whose hash is its name, so tests can state which images are "the same".</summary>
    public static CardImage Image(string name, string? hash = null) =>
        new($"/source/{name}", $"/cache/{Guid.NewGuid():N}", hash ?? name);
}
