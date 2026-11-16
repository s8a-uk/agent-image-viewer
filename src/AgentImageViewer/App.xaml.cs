using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using AgentImageViewer.Core;
using AgentImageViewer.Core.Cards;
using AgentImageViewer.Core.Images;
using AgentImageViewer.Core.Ipc;
using AgentImageViewer.Core.Settings;
using AgentImageViewer.Imaging;
using AgentImageViewer.Services;
using AgentImageViewer.Views;

namespace AgentImageViewer;

public partial class App : Application
{
    private SettingsStore _settingsStore = null!;
    private AppSettings _settings = null!;
    private MainWindow _mainWindow = null!;
    private ImageCache? _cache;
    private TrayIcon? _tray;
    private PipeServer? _server;
    private SettingsWindow? _settingsWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        // Signing out or shutting down Windows ends the process without ExitApp.
        SessionEnding += (_, _) => ReleaseResources();

        _settingsStore = new SettingsStore(AppPaths.SettingsFile);
        _settings = _settingsStore.Load();
        _cache = new ImageCache(AppPaths.CacheDirectory);
        var images = new ThumbnailImageStore(_cache);
        var board = new CardBoard(images, _settings.MaxCards);

        // Created but not shown: the app starts with only the tray icon.
        _mainWindow = new MainWindow(_settings, _settingsStore, board);
        _mainWindow.SettingsRequested += (_, _) => OpenSettings();
        _mainWindow.ExitRequested += (_, _) => ExitApp();
        MainWindow = _mainWindow;

        // Create the HWND without showing the window: the snapper hooks it and the tray menu uses it as owner.
        var mainHandle = new WindowInteropHelper(_mainWindow).EnsureHandle();
        ThemeService.Apply(_settings.Theme, _mainWindow);

        _tray = new TrayIcon(
            mainHandle,
            isShown: () => _mainWindow.IsVisible,
            canShow: () => _mainWindow.HasCards,
            onToggle: _mainWindow.ToggleVisibility,
            onSettings: OpenSettings,
            onClear: _mainWindow.ClearCards,
            onExit: ExitApp);

        var receiver = new CardReceiver(
            images,
            new WpfUiDispatcher(Dispatcher),
            TimeProvider.System,
            onCard: _mainWindow.AddCard,
            onShow: _mainWindow.ShowWithoutFocus,
            onQuit: ExitApp);
        _server = new PipeServer(PipeProtocol.PipeName, receiver);
        _server.Start();
    }

    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings, _settingsStore);
        try
        {
            if (_settingsWindow.ShowDialog() == true)
            {
                ThemeService.Apply(_settings.Theme, _mainWindow);
                _mainWindow.ApplySettings();
            }
        }
        finally
        {
            _settingsWindow = null;
        }
    }

    private void ExitApp()
    {
        ReleaseResources();
        _mainWindow.AllowClose();
        Shutdown();
    }

    private void ReleaseResources()
    {
        _server?.Dispose();
        _tray?.Dispose();
        // The copies are only for the cards on screen; don't leave the user's screenshots on disk.
        _cache?.Close();
    }

    // Nothing sensible can continue after an unexpected UI exception. Leave a trace for a bug report, empty the cache
    // and remove the tray icon, which would otherwise stay behind as a ghost until the mouse passes over it.
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.ErrorLog)!);
            File.AppendAllText(AppPaths.ErrorLog, $"{DateTimeOffset.Now:O} {AppInfo.Version}{Environment.NewLine}{e.Exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Crashing is the outcome either way.
        }

        ReleaseResources();
    }
}
