using System.Windows.Media.Imaging;
using AgentImageViewer.Core.Images;

namespace AgentImageViewer.Imaging;

/// <summary>A cached image together with its decoded thumbnail, ready to bind to.</summary>
public sealed record ThumbnailImage(string SourcePath, string CachedPath, string Hash, BitmapSource Thumbnail)
    : CardImage(SourcePath, CachedPath, Hash);
