using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace AgentImageViewer;

/// <summary>
/// Tray icon via WinForms <see cref="Forms.NotifyIcon"/>, which works without any shown window
/// (the WPF-UI tray control needs a window that has been displayed once).
/// The right-click menu is a WPF ContextMenu so it gets the Fluent styles.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly ContextMenu _menu;
    private readonly IntPtr _menuOwner;
    private readonly Drawing.Icon _image;

    /// <param name="menuOwner">HWND made foreground while the menu is open, so clicking elsewhere closes it.</param>
    /// <param name="isShown">Whether the window is visible; shown as the check mark of "表示".</param>
    /// <param name="canShow">Whether there is anything to show; "表示" is disabled otherwise.</param>
    public TrayIcon(
        IntPtr menuOwner,
        Func<bool> isShown,
        Func<bool> canShow,
        Action onToggle,
        Action onSettings,
        Action onClear,
        Action onExit)
    {
        _menuOwner = menuOwner;
        _menu = new ContextMenu { Placement = PlacementMode.MousePoint };

        // The WPF UI menu style draws the check mark only for checkable items. The state is set from the window
        // each time the menu opens, so the toggle a click makes on its own does not matter.
        var showItem = CreateItem("表示", onToggle);
        showItem.IsCheckable = true;
        _menu.Opened += (_, _) =>
        {
            showItem.IsChecked = isShown();
            showItem.IsEnabled = showItem.IsChecked || canShow();
        };
        _menu.Items.Add(showItem);
        _menu.Items.Add(CreateItem("履歴をクリア", onClear));
        _menu.Items.Add(new Separator());
        _menu.Items.Add(CreateItem("設定...", onSettings));
        _menu.Items.Add(CreateItem("終了", onExit));

        _image = LoadIcon();
        _icon = new Forms.NotifyIcon
        {
            Text = "Agent Image Viewer",
            Icon = _image,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                onToggle();
            }
            else if (e.Button == Forms.MouseButtons.Right)
            {
                ShowMenu();
            }
        };
    }

    private void ShowMenu()
    {
        SetForegroundWindow(_menuOwner);
        _menu.IsOpen = true;
    }

    private static MenuItem CreateItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>The app icon (Assets/app.ico, also the exe's icon) at the size Windows uses in the tray.</summary>
    private static Drawing.Icon LoadIcon()
    {
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream("AgentImageViewer.app.ico")
            ?? throw new InvalidOperationException("The app icon is not embedded.");
        return new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _image.Dispose();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

}
