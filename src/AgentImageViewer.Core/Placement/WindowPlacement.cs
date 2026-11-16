using AgentImageViewer.Core.Settings;

namespace AgentImageViewer.Core.Placement;

/// <summary>A rectangle in screen coordinates.</summary>
public readonly record struct ScreenRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;

    public bool Contains(double x, double y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

/// <param name="DeviceName">Stable name of the display, such as <c>\\.\DISPLAY2</c>.</param>
public sealed record DisplayMonitor(string DeviceName, ScreenRect Bounds, ScreenRect WorkArea, bool IsPrimary);

/// <summary>Where the window goes and whether it starts glued to the bottom of its work area.</summary>
public readonly record struct WindowPosition(double Left, double Top, bool AnchorBottom);

/// <summary>Decides where the main window appears on startup from the saved settings and the current displays.</summary>
public static class WindowPlacement
{
    /// <summary>Gap between the default position and the right edge of the work area.</summary>
    public const double DefaultMargin = 12;

    // The saved position counts as visible when a point this far inside the window's top-left corner is on the monitor,
    // so the user can still grab the window.
    private const double GrabInset = 20;

    /// <summary>
    /// The saved monitor wins when it is connected: the window goes back to the saved position on it, or to its default
    /// corner if that position is no longer on it. A window docked to the bottom edge is put back on that edge. Without the saved monitor, the saved position is kept if it is on any
    /// monitor; otherwise the window goes to the default corner of the primary monitor.
    /// </summary>
    public static WindowPosition Resolve(AppSettings settings, IReadOnlyList<DisplayMonitor> monitors, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(monitors);
        if (monitors.Count == 0)
        {
            throw new ArgumentException("At least one monitor is required.", nameof(monitors));
        }

        var savedMonitor = settings.WindowMonitor is { } name
            ? monitors.FirstOrDefault(m => string.Equals(m.DeviceName, name, StringComparison.OrdinalIgnoreCase))
            : null;

        if (settings.WindowLeft is { } left && settings.WindowTop is { } top)
        {
            bool IsOn(DisplayMonitor monitor) => monitor.Bounds.Contains(left + GrabInset, top + GrabInset);

            var monitor = savedMonitor is not null
                ? (IsOn(savedMonitor) ? savedMonitor : null)
                : monitors.FirstOrDefault(IsOn);
            if (monitor is not null)
            {
                // A docked window was saved at its height back then; dock it at its height now.
                return settings.AnchorBottom
                    ? new WindowPosition(left, monitor.WorkArea.Bottom - height, AnchorBottom: true)
                    : new WindowPosition(left, top, AnchorBottom: false);
            }
        }

        var target = savedMonitor ?? monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
        return DefaultOn(target, width, height);
    }

    /// <summary>The bottom-right corner of the monitor's work area, anchored to the bottom.</summary>
    public static WindowPosition DefaultOn(DisplayMonitor monitor, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        var work = monitor.WorkArea;
        return new WindowPosition(work.Right - width - DefaultMargin, work.Bottom - height, AnchorBottom: true);
    }
}
