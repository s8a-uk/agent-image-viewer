namespace AgentImageViewer.Core.Images;

/// <summary>An image copied into the viewer's cache.</summary>
/// <param name="SourcePath">Where the image was read from; may no longer exist.</param>
/// <param name="CachedPath">The viewer's own copy.</param>
/// <param name="Hash">Content hash; a re-captured screenshot at the same path counts as a new image.</param>
public record CardImage(string SourcePath, string CachedPath, string Hash)
{
    public string FileName => Path.GetFileName(SourcePath);
}
