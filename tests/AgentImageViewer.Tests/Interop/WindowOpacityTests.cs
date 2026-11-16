using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using AgentImageViewer.Interop;

namespace AgentImageViewer.Tests.Interop;

public sealed class WindowOpacityTests
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x00080000;

    [Fact]
    public void A_translucent_window_stays_layered_with_the_requested_alpha()
    {
        RunOnStaThread(() =>
        {
            var window = new Window();
            var opacity = new WindowOpacity(window) { Value = 0.5 };
            var hwnd = new WindowInteropHelper(window).EnsureHandle();

            try
            {
                Assert.True(IsLayered(hwnd));
                Assert.True(GetLayeredWindowAttributes(hwnd, out _, out var alpha, out _));
                Assert.Equal(128, alpha);

                opacity.Value = 0.8;
                Assert.True(GetLayeredWindowAttributes(hwnd, out _, out alpha, out _));
                Assert.Equal(204, alpha);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void An_opaque_window_is_not_layered()
    {
        RunOnStaThread(() =>
        {
            var window = new Window();
            var opacity = new WindowOpacity(window) { Value = 0.5 };
            var hwnd = new WindowInteropHelper(window).EnsureHandle();

            try
            {
                opacity.Value = 1.0;
                Assert.False(IsLayered(hwnd));
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static bool IsLayered(IntPtr hwnd) => (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_LAYERED) != 0;

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint crKey, out byte bAlpha, out uint dwFlags);
}
