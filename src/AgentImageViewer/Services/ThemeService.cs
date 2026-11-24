using System.Windows;
using AgentImageViewer.Core.Settings;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace AgentImageViewer.Services;

public static class ThemeService
{
    private static AppTheme _mode = AppTheme.System;
    private static bool _watching;

    /// <summary>Applies the theme app-wide; <paramref name="window"/> is used to follow OS theme changes.</summary>
    public static void Apply(AppTheme mode, Window window)
    {
        _mode = mode;
        if (mode == AppTheme.System)
        {
            ApplicationThemeManager.ApplySystemTheme();
            WatchSystemTheme(window);
            return;
        }

        // Only set once the window is loaded, so UnWatch is safe here.
        if (_watching)
        {
            SystemThemeWatcher.UnWatch(window);
            _watching = false;
        }

        ApplicationThemeManager.Apply(
            mode == AppTheme.Light ? ApplicationTheme.Light : ApplicationTheme.Dark,
            WindowBackdropType.Mica);
    }

    // WPF UI refuses to stop watching a window that is not loaded yet, and the main window stays unloaded until it is
    // first shown. So start watching only once it is loaded, by which time the setting may have changed again.
    private static void WatchSystemTheme(Window window)
    {
        if (_watching)
        {
            return;
        }

        if (!window.IsLoaded)
        {
            window.Loaded -= OnWindowLoaded;
            window.Loaded += OnWindowLoaded;
            return;
        }

        SystemThemeWatcher.Watch(window);
        _watching = true;
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        var window = (Window)sender;
        window.Loaded -= OnWindowLoaded;
        if (_mode == AppTheme.System)
        {
            // The OS theme may have changed while nothing was watching.
            ApplicationThemeManager.ApplySystemTheme();
            WatchSystemTheme(window);
        }
    }
}
