using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AgentImageViewer.Imaging;

namespace AgentImageViewer.Views;

/// <summary>
/// Full-screen overlay on the owner's monitor showing the image with its card's full message
/// just above it. The wheel zooms around the cursor (never below the opening size), dragging pans,
/// and a click without dragging closes it.
/// </summary>
public partial class ImagePreviewWindow : Window
{
    // With a message, the image leaves this much of the height free above it for the text.
    private const double ImageMaxHeightRatioWithMessage = 0.75;
    private const double MessageGap = 12;
    private const double ZoomStep = 1.25;
    private const double MaxZoom = 16;

    private readonly bool _hasMessage;
    private readonly (int Width, int Height) _pixelSize;
    private Point? _pressPoint;
    private Point _lastPanPoint;
    private bool _panning;

    public ImagePreviewWindow(ThumbnailImage image, string text, Window owner)
    {
        InitializeComponent();

        MessageText.Text = text.Trim();
        _hasMessage = MessageText.Text.Length > 0;
        MessagePanel.Visibility = _hasMessage ? Visibility.Visible : Visibility.Collapsed;

        // Open at once with the thumbnail, sized as the full image, and swap in the full image once it is decoded
        // off the UI thread: a large image takes a while, and the UI thread also answers the pipe.
        PreviewImage.Source = image.Thumbnail;
        _pixelSize = TryReadPixelSize(image.CachedPath) ?? (image.Thumbnail.PixelWidth, image.Thumbnail.PixelHeight);
        _ = LoadFullImageAsync(image.CachedPath);

        // Place on the owner's monitor before maximizing, so the overlay covers that monitor.
        Left = owner.Left;
        Top = owner.Top;
        Width = owner.ActualWidth;
        Height = owner.ActualHeight;
        SourceInitialized += (_, _) => WindowState = WindowState.Maximized;
        Stage.SizeChanged += (_, _) =>
        {
            SizeImage();
            // The image may move without resizing; re-attach once layout has settled.
            Dispatcher.BeginInvoke(AttachMessageToImage, DispatcherPriority.Loaded);
        };
        PreviewImage.SizeChanged += (_, _) => AttachMessageToImage();
        DpiChanged += (_, _) => SizeImage();
        Loaded += (_, _) => Activate();
    }

    /// <summary>
    /// Shows the image pixel-for-pixel (one image pixel per screen pixel, ignoring the file's DPI
    /// and the display scaling), shrinking it uniformly only when it does not fit.
    /// </summary>
    private void SizeImage()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = _pixelSize.Width / dpi.DpiScaleX;
        var height = _pixelSize.Height / dpi.DpiScaleY;

        var maxHeight = _hasMessage ? Stage.ActualHeight * ImageMaxHeightRatioWithMessage : Stage.ActualHeight;
        var scale = Math.Min(1.0, Math.Min(Stage.ActualWidth / width, maxHeight / height));
        PreviewImage.Width = width * scale;
        PreviewImage.Height = height * scale;
        ZoomTransform.Matrix = Matrix.Identity;
    }

    private static bool IsDecodeFailure(Exception ex) => ex is IOException or UnauthorizedAccessException
        or NotSupportedException or ArgumentException or FileFormatException or OverflowException or OutOfMemoryException;

    private static (int Width, int Height)? TryReadPixelSize(string path)
    {
        try
        {
            return BitmapLoader.ReadPixelSize(path);
        }
        catch (Exception ex) when (IsDecodeFailure(ex))
        {
            return null;
        }
    }

    private async Task LoadFullImageAsync(string path)
    {
        try
        {
            // Frozen, so it can be handed from the worker thread to this window.
            PreviewImage.Source = await Task.Run(() => BitmapLoader.Load(path));
        }
        catch (Exception ex) when (IsDecodeFailure(ex))
        {
            // Damaged, removed, or too large even when capped: the thumbnail stays. OutOfMemoryException here is one
            // failed large allocation, not a dying process.
        }
    }

    /// <summary>Puts the message panel's bottom a small gap above the image's top edge.</summary>
    private void AttachMessageToImage()
    {
        if (!_hasMessage)
        {
            return;
        }

        // Layout position (the image is centered), so zooming does not move the text.
        var imageTop = (Stage.ActualHeight - PreviewImage.ActualHeight) / 2;
        MessagePanel.Margin = new Thickness(0, 0, 0, Stage.ActualHeight - imageTop + MessageGap);

        // Long text is trimmed to the space above the image.
        var padding = MessagePanel.Padding.Top + MessagePanel.Padding.Bottom;
        MessageText.MaxHeight = Math.Max(MessageText.LineHeight, imageTop - MessageGap - padding);
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var matrix = ZoomTransform.Matrix;
        var factor = e.Delta > 0 ? ZoomStep : 1 / ZoomStep;
        var zoom = matrix.M11 * factor;
        if (zoom > MaxZoom)
        {
            return;
        }

        if (zoom <= 1.0)
        {
            // Back to the opening size and position.
            ZoomTransform.Matrix = Matrix.Identity;
            return;
        }

        // Keep the image point under the cursor fixed while zooming.
        var cursor = e.GetPosition(Stage);
        var origin = PreviewImage.TranslatePoint(new Point(0, 0), Stage);
        matrix.ScaleAt(factor, factor, cursor.X - origin.X + matrix.OffsetX, cursor.Y - origin.Y + matrix.OffsetY);
        ZoomTransform.Matrix = matrix;
        e.Handled = true;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressPoint = _lastPanPoint = e.GetPosition(this);
        _panning = false;
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressPoint is not { } start || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var point = e.GetPosition(this);
        if (!_panning)
        {
            var moved = point - start;
            _panning = Math.Abs(moved.X) >= SystemParameters.MinimumHorizontalDragDistance
                || Math.Abs(moved.Y) >= SystemParameters.MinimumVerticalDragDistance;
        }

        // Panning only means something once zoomed in.
        if (_panning && ZoomTransform.Matrix.M11 > 1.0)
        {
            var matrix = ZoomTransform.Matrix;
            matrix.Translate(point.X - _lastPanPoint.X, point.Y - _lastPanPoint.Y);
            ZoomTransform.Matrix = matrix;
        }

        _lastPanPoint = point;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var wasClick = _pressPoint is not null && !_panning;
        _pressPoint = null;
        _panning = false;
        ReleaseMouseCapture();

        if (wasClick)
        {
            Close();
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }
}
