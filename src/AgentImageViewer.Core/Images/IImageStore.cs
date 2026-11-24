namespace AgentImageViewer.Core.Images;

/// <summary>Keeps the viewer's own copies of the images shown on cards.</summary>
public interface IImageStore
{
    /// <summary>Takes a copy of the image. Returns null if it is not a readable image.</summary>
    CardImage? Import(string sourcePath);

    /// <summary>Deletes the copies of images that are no longer shown.</summary>
    void Release(IEnumerable<CardImage> images);
}
