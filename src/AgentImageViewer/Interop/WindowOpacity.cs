using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AgentImageViewer.Interop;

/// <summary>
/// Makes the whole top-level window (backdrop included) translucent via a layered window.
/// WPF's Opacity only fades the content and needs AllowsTransparency, which WindowChrome/Mica do not support.
/// </summary>
public sealed class WindowOpacity
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WM_STYLECHANGING = 0x007C;
    private const uint LWA_ALPHA = 0x2;

    private readonly Window _window;
    private double _value = 1.0;

    public WindowOpacity(Window window)
    {
        _window = window;
        _window.SourceInitialized += (_, _) =>
        {
            HwndSource.FromHwnd(Handle)?.AddHook(WndProc);
            Apply();
        };
    }

    /// <summary>Opacity of the whole window, 0 to 1.</summary>
    public double Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0, 1);
            Apply();
        }
    }

    private bool IsTranslucent => _value < 1.0;

    private IntPtr Handle => new WindowInteropHelper(_window).Handle;

    private void Apply()
    {
        var hwnd = Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        if (!IsTranslucent)
        {
            // Fully opaque: drop the layered style so nothing changes from a normal window.
            _ = SetWindowLong(hwnd, GWL_EXSTYLE, style & ~WS_EX_LAYERED);
            return;
        }

        _ = SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_LAYERED);
        SetLayeredWindowAttributes(hwnd, 0, (byte)Math.Round(_value * 255), LWA_ALPHA);
    }

    // WPF strips WS_EX_LAYERED from windows without AllowsTransparency whenever the extended style
    // changes, which would silently undo Apply. Keep the bit while the window should be translucent.
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_STYLECHANGING && wParam.ToInt64() == GWL_EXSTYLE && IsTranslucent)
        {
            var styles = Marshal.PtrToStructure<STYLESTRUCT>(lParam);
            styles.styleNew |= WS_EX_LAYERED;
            Marshal.StructureToPtr(styles, lParam, fDeleteOld: false);
            handled = true;
        }

        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STYLESTRUCT
    {
        public int styleOld;
        public int styleNew;
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
}
