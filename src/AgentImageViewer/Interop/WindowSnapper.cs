using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using AgentImageViewer.Core.Placement;

namespace AgentImageViewer.Interop;

/// <summary>
/// Snaps a window to the work-area edges of its monitor while it is dragged,
/// keeps it glued to the bottom edge when its height changes, and limits
/// user resizing to the width (the height follows the content).
/// Also reports the monitors and moves the window in physical screen pixels, which, unlike WPF's Left and Top,
/// mean the same on every monitor when their scale factors differ.
/// </summary>
public sealed class WindowSnapper
{
    private const int WM_WINDOWPOSCHANGING = 0x0046;
    private const int WM_SIZING = 0x0214;
    private const int WM_MOVING = 0x0216;
    private const int WM_ENTERSIZEMOVE = 0x0231;
    private const int WM_EXITSIZEMOVE = 0x0232;
    private const int SnapDistanceDip = 16;
    private const int AnchorToleranceDip = 2;

    private readonly Window _window;
    private HwndSource? _source;
    private bool _inSizeMove;
    private bool _resized;
    private bool _dragging;
    private POINT _dragStartCursor;
    private RECT _dragStartRect;

    public WindowSnapper(Window window)
    {
        _window = window;
        _window.SourceInitialized += (_, _) =>
        {
            // FromVisual is still null here when the HWND is created by EnsureHandle without showing.
            _source = HwndSource.FromHwnd(new WindowInteropHelper(_window).Handle);
            _source?.AddHook(WndProc);

            // Without a maximize box, dragging to the top of the screen does not maximize (Aero Snap).
            var hwnd = new WindowInteropHelper(_window).Handle;
            _ = SetWindowLong(hwnd, GWL_STYLE, GetWindowLong(hwnd, GWL_STYLE) & ~WS_MAXIMIZEBOX);
        };
    }

    /// <summary>True while the window's bottom edge sits on the work-area bottom.</summary>
    public bool IsAnchoredBottom { get; set; }

    /// <summary>Raised when the user finishes moving or resizing the window.</summary>
    public event EventHandler<SizeMoveCompletedEventArgs>? MoveCompleted;

    /// <summary>The window's outer rectangle in physical screen pixels, or null before it has a handle.</summary>
    public ScreenRect? Bounds => _source is not null && GetWindowRect(_source.Handle, out var rect) ? ToScreenRect(rect, 1.0) : null;

    private double DpiScale => _source?.CompositionTarget.TransformToDevice.M11 ?? 1.0;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_WINDOWPOSCHANGING when IsAnchoredBottom && !_inSizeMove:
                KeepBottomAnchored(hwnd, lParam);
                break;

            case WM_ENTERSIZEMOVE:
                _inSizeMove = true;
                _resized = false;
                _dragging = GetCursorPos(out _dragStartCursor) && GetWindowRect(hwnd, out _dragStartRect);
                break;

            case WM_SIZING:
                _resized = true;
                // Keep the vertical edges where they are: only the width is user-resizable.
                if (GetWindowRect(hwnd, out var before))
                {
                    var sizing = Marshal.PtrToStructure<RECT>(lParam);
                    sizing.Top = before.Top;
                    sizing.Bottom = before.Bottom;
                    Marshal.StructureToPtr(sizing, lParam, fDeleteOld: false);
                    handled = true;
                    return 1;
                }

                break;

            case WM_MOVING:
                var rect = Marshal.PtrToStructure<RECT>(lParam);
                // Windows builds the proposed rect from the rect we returned last time, so a snapped
                // position would stick while the mouse moves slowly. Recompute the unsnapped position
                // from the cursor's total travel instead.
                if (_dragging && GetCursorPos(out var cursor))
                {
                    var width = rect.Right - rect.Left;
                    var height = rect.Bottom - rect.Top;
                    rect.Left = _dragStartRect.Left + (cursor.X - _dragStartCursor.X);
                    rect.Top = _dragStartRect.Top + (cursor.Y - _dragStartCursor.Y);
                    rect.Right = rect.Left + width;
                    rect.Bottom = rect.Top + height;
                }

                Snap(ref rect, WorkArea(rect), (int)Math.Round(SnapDistanceDip * DpiScale));
                Marshal.StructureToPtr(rect, lParam, fDeleteOld: false);
                handled = true;
                return 1;

            case WM_EXITSIZEMOVE:
                _inSizeMove = false;
                _dragging = false;
                if (GetWindowRect(hwnd, out var current))
                {
                    var tolerance = AnchorToleranceDip * DpiScale;
                    IsAnchoredBottom = Math.Abs(current.Bottom - WorkArea(current).Bottom) <= tolerance;
                }

                MoveCompleted?.Invoke(this, new SizeMoveCompletedEventArgs(_resized));
                break;
        }

        return IntPtr.Zero;
    }

    private static void Snap(ref RECT rect, RECT work, int distance)
    {
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;

        if (Math.Abs(rect.Left - work.Left) <= distance)
        {
            rect.Left = work.Left;
        }
        else if (Math.Abs(rect.Right - work.Right) <= distance)
        {
            rect.Left = work.Right - width;
        }

        if (Math.Abs(rect.Top - work.Top) <= distance)
        {
            rect.Top = work.Top;
        }
        else if (Math.Abs(rect.Bottom - work.Bottom) <= distance)
        {
            rect.Top = work.Bottom - height;
        }

        rect.Right = rect.Left + width;
        rect.Bottom = rect.Top + height;
    }

    /// <summary>Device name of the monitor the window is on (the nearest one if it is off-screen).</summary>
    public string? CurrentMonitorName
    {
        get
        {
            if (_source is null)
            {
                return null;
            }

            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            var monitor = MonitorFromWindow(_source.Handle, MONITOR_DEFAULTTONEAREST);
            return GetMonitorInfo(monitor, ref info) ? info.szDevice : null;
        }
    }

    /// <summary>Work area of the monitor the window is on, in device-independent units.</summary>
    public ScreenRect? CurrentWorkArea
    {
        get
        {
            if (_source is null)
            {
                return null;
            }

            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            var monitor = MonitorFromWindow(_source.Handle, MONITOR_DEFAULTTONEAREST);
            return GetMonitorInfo(monitor, ref info) ? ToScreenRect(info.rcWork, DpiScale) : null;
        }
    }

    /// <summary>Moves the window's top-left corner to the given physical screen position, keeping its size.</summary>
    public void MoveTo(double left, double top)
    {
        if (_source is not null)
        {
            SetWindowPos(_source.Handle, IntPtr.Zero, (int)Math.Round(left), (int)Math.Round(top), 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
    }

    /// <summary>All connected monitors, in physical screen pixels.</summary>
    public static IReadOnlyList<DisplayMonitor> GetMonitors()
    {
        var monitors = new List<DisplayMonitor>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                monitors.Add(new DisplayMonitor(
                    info.szDevice,
                    ToScreenRect(info.rcMonitor, 1.0),
                    ToScreenRect(info.rcWork, 1.0),
                    (info.dwFlags & MONITORINFOF_PRIMARY) != 0));
            }

            return true;
        }, IntPtr.Zero);
        return monitors;
    }

    private static ScreenRect ToScreenRect(RECT rect, double scale) =>
        new(rect.Left / scale, rect.Top / scale, (rect.Right - rect.Left) / scale, (rect.Bottom - rect.Top) / scale);

    // Adjusts the move/resize that is about to happen, so the window never shows a frame hanging below the work area.
    // Handling SizeChanged instead is too late: WPF raises it before the window has its new size.
    private static void KeepBottomAnchored(IntPtr hwnd, IntPtr lParam)
    {
        var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
        if ((pos.flags & SWP_NOSIZE) != 0 || !GetWindowRect(hwnd, out var current))
        {
            return;
        }

        if ((pos.flags & SWP_NOMOVE) != 0)
        {
            pos.x = current.Left;
            pos.y = current.Top;
            pos.flags &= ~SWP_NOMOVE;
        }

        var proposed = new RECT { Left = pos.x, Top = pos.y, Right = pos.x + pos.cx, Bottom = pos.y + pos.cy };
        pos.y = WorkArea(proposed).Bottom - pos.cy;
        Marshal.StructureToPtr(pos, lParam, fDeleteOld: false);
    }

    private static RECT WorkArea(RECT rect)
    {
        var monitor = MonitorFromRect(ref rect, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        return GetMonitorInfo(monitor, ref info) ? info.rcWork : rect;
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint MONITORINFOF_PRIMARY = 1;
    private const int GWL_STYLE = -16;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}

/// <param name="Resized">Whether the user changed the window's size, not just its position.</param>
public sealed class SizeMoveCompletedEventArgs(bool resized) : EventArgs
{
    public bool Resized { get; } = resized;
}
