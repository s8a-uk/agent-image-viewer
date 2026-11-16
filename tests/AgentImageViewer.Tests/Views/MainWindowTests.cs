using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using AgentImageViewer.Core.Cards;
using AgentImageViewer.Core.Images;
using AgentImageViewer.Core.Settings;
using AgentImageViewer.Imaging;
using AgentImageViewer.Services;
using AgentImageViewer.Tests.TestSupport;
using AgentImageViewer.Views;

namespace AgentImageViewer.Tests.Views;

/// <summary>The main window as the app uses it: created hidden, shown for cards, themed from the settings.</summary>
[Collection(AppTests.Name)]
public sealed class MainWindowTests(AppThread app) : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void The_theme_can_leave_System_before_the_window_was_ever_shown()
    {
        app.Invoke(() =>
        {
            var window = CreateWindow();
            try
            {
                ThemeService.Apply(AppTheme.System, window);
                ThemeService.Apply(AppTheme.Light, window);
                ThemeService.Apply(AppTheme.System, window);

                window.AddCard(Card("s", "g"));
                AppThread.DoEvents();
                ThemeService.Apply(AppTheme.Dark, window);
                ThemeService.Apply(AppTheme.System, window);
            }
            finally
            {
                Close(window);
            }
        });
    }

    [Fact]
    public void Closing_the_window_only_hides_it_and_the_next_card_shows_it_again()
    {
        app.Invoke(() =>
        {
            var window = CreateWindow();
            try
            {
                window.AddCard(Card("s", "first"));
                AppThread.DoEvents();
                Assert.True(window.IsVisible);

                window.Close();
                AppThread.DoEvents();
                Assert.False(window.IsVisible);

                window.AddCard(Card("s", "second"));
                AppThread.DoEvents();
                Assert.True(window.IsVisible);
            }
            finally
            {
                Close(window);
            }
        });
    }

    [Fact]
    public void A_thumbnail_is_a_named_button_that_opens_the_preview()
    {
        app.Invoke(() =>
        {
            var window = CreateWindow();
            try
            {
                window.AddCard(Card("s", "g"));
                AppThread.DoEvents();

                var button = Descendants(window).OfType<Button>().Single(b => b.Tag is "Thumbnail");
                var peer = (ButtonAutomationPeer)UIElementAutomationPeer.CreatePeerForElement(button);
                Assert.Equal("g.png", peer.GetName());
                Assert.True(button.Focusable);

                ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
                AppThread.DoEvents();

                var preview = Assert.Single(Application.Current.Windows.OfType<ImagePreviewWindow>());
                preview.Close();
            }
            finally
            {
                Close(window);
            }
        });
    }

    private MainWindow CreateWindow()
    {
        var settings = new AppSettings { PlaySound = false, Topmost = false };
        var window = new MainWindow(settings, new SettingsStore(_temp.File("settings.json")), new CardBoard(new NullImageStore(), settings.MaxCards));
        // As App does: the HWND exists long before the window is first shown.
        new WindowInteropHelper(window).EnsureHandle();
        return window;
    }

    private ResponseCard Card(string session, string group)
    {
        var path = TestImages.WritePng(_temp.File($"{group}.png"), 4, 4);
        var image = new ThumbnailImage(path, path, group, BitmapLoader.Load(path));
        return new ResponseCard(session, group, "Test", null, "text", [image], DateTime.Now);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static void Close(MainWindow window)
    {
        window.AllowClose();
        window.Close();
    }

    private sealed class NullImageStore : IImageStore
    {
        public CardImage? Import(string sourcePath) => null;

        public void Release(IEnumerable<CardImage> images)
        {
        }
    }
}
