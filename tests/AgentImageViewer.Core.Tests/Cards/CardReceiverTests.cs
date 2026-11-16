using AgentImageViewer.Core.Cards;
using AgentImageViewer.Core.Images;
using AgentImageViewer.Core.Ipc;
using AgentImageViewer.Core.Tests.TestSupport;

namespace AgentImageViewer.Core.Tests.Cards;

public sealed class CardReceiverTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly TimeZoneInfo Tokyo = TimeZoneInfo.CreateCustomTimeZone("Test/Tokyo", TimeSpan.FromHours(9), "Tokyo", "Tokyo");

    private readonly TempDirectory _temp = new();
    private readonly string _cacheDirectory;
    private readonly InlineUiDispatcher _ui = new();
    private readonly List<ResponseCard> _cards = [];
    private int _shown;
    private int _quit;

    public CardReceiverTests()
    {
        _cacheDirectory = Path.Combine(_temp.Path, "cache");
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task A_post_becomes_a_card_with_the_imported_images()
    {
        var receiver = Receiver();
        var one = _temp.WriteFile("one.png", "1");
        var two = _temp.WriteFile("two.jpg", "2");

        await receiver.HandleAsync(new ViewerMessage
        {
            Command = ViewerMessage.Post,
            Session = "s-1",
            Group = "g-1",
            SessionName = "My session",
            Cwd = "/home/me/project",
            Text = " The message. ",
            Paths = [one, two],
        });

        var card = Assert.Single(_cards);
        Assert.Equal("s-1", card.Session);
        Assert.Equal("g-1", card.Group);
        Assert.Equal("My session", card.Title);
        Assert.Equal("The message.", card.Text);
        Assert.Equal([one, two], card.Images.Select(i => i.SourcePath));
        Assert.All(card.Images, image => Assert.True(File.Exists(image.CachedPath)));
        Assert.Equal(1, _ui.Invocations);
    }

    [Fact]
    public async Task The_card_is_stamped_with_the_local_time()
    {
        var receiver = Receiver();

        await receiver.HandleAsync(Post(_temp.WriteFile("one.png", "1")));

        var receivedAt = Assert.Single(_cards).ReceivedAt;
        Assert.Equal(new DateTime(2026, 1, 2, 12, 4, 5), receivedAt);
    }

    [Fact]
    public async Task The_same_picture_posted_twice_in_one_message_is_shown_once()
    {
        var receiver = Receiver();
        var original = _temp.WriteFile("original.png", "same");
        var copy = _temp.WriteFile("copy.png", "same");
        var different = _temp.WriteFile("different.png", "different");

        await receiver.HandleAsync(Post(original, copy, different));

        var card = Assert.Single(_cards);
        Assert.Equal([original, different], card.Images.Select(i => i.SourcePath));
        Assert.Equal(card.Images.Select(i => i.CachedPath).Order(), Directory.GetFiles(_cacheDirectory).Order());
    }

    [Fact]
    public async Task Files_that_cannot_be_imported_are_left_out()
    {
        var receiver = Receiver();
        var image = _temp.WriteFile("one.png", "1");

        await receiver.HandleAsync(Post(_temp.WriteFile("notes.txt", "text"), image, _temp.File("missing.png")));

        Assert.Equal([image], Assert.Single(_cards).Images.Select(i => i.SourcePath));
    }

    [Fact]
    public async Task A_post_without_any_usable_image_creates_no_card()
    {
        var receiver = Receiver();

        await receiver.HandleAsync(Post(_temp.WriteFile("notes.txt", "text"), _temp.File("missing.png")));
        await receiver.HandleAsync(Post());

        Assert.Empty(_cards);
        Assert.Equal(0, _ui.Invocations);
    }

    [Fact]
    public async Task A_post_without_a_session_or_paths_is_ignored()
    {
        var receiver = Receiver();
        var image = _temp.WriteFile("one.png", "1");

        await receiver.HandleAsync(new ViewerMessage { Command = ViewerMessage.Post, Paths = [image] });
        await receiver.HandleAsync(new ViewerMessage { Command = ViewerMessage.Post, Session = "s" });

        Assert.Empty(_cards);
        Assert.Empty(Directory.GetFiles(_cacheDirectory));
    }

    [Fact]
    public async Task Show_asks_the_ui_to_show_the_window()
    {
        await Receiver().HandleAsync(new ViewerMessage { Command = ViewerMessage.Show });

        Assert.Equal(1, _shown);
        Assert.Equal(0, _quit);
        Assert.Empty(_cards);
    }

    [Fact]
    public async Task Quit_asks_the_ui_to_exit()
    {
        await Receiver().HandleAsync(new ViewerMessage { Command = ViewerMessage.Quit });

        Assert.Equal(1, _quit);
        Assert.Equal(0, _shown);
    }

    [Fact]
    public async Task Quit_does_not_wait_for_the_ui_so_the_acknowledgement_can_still_be_sent()
    {
        var ui = new NeverCompletingUiDispatcher();
        var receiver = new CardReceiver(new RecordingImageStore(), ui, TimeProvider.System, _cards.Add, () => _shown++, () => _quit++);

        var handling = receiver.HandleAsync(new ViewerMessage { Command = ViewerMessage.Quit });

        await handling.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(1, ui.Invocations);
    }

    [Theory]
    [InlineData("")]
    [InlineData("POST")]
    [InlineData("restart")]
    public async Task Unknown_commands_are_ignored(string command)
    {
        await Receiver().HandleAsync(new ViewerMessage { Command = command, Session = "s", Paths = [_temp.WriteFile("one.png", "1")] });

        Assert.Empty(_cards);
        Assert.Equal(0, _ui.Invocations);
    }

    [Fact]
    public async Task Only_the_first_images_of_an_oversized_post_are_imported()
    {
        var receiver = Receiver();
        var paths = Enumerable.Range(0, CardReceiver.MaxImagesPerPost + 5)
            .Select(i => _temp.WriteFile($"{i}.png", $"{i}"))
            .ToList();

        await receiver.HandleAsync(Post([.. paths]));

        Assert.Equal(paths.Take(CardReceiver.MaxImagesPerPost), Assert.Single(_cards).Images.Select(i => i.SourcePath));
    }

    [Fact]
    public async Task Images_imported_before_a_failure_are_released()
    {
        var cache = new ImageCache(_cacheDirectory);
        var receiver = Receiver(new FailingStore(cache, failOn: "broken.png"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            receiver.HandleAsync(Post(_temp.WriteFile("one.png", "1"), _temp.WriteFile("broken.png", "2"))));

        Assert.Empty(_cards);
        Assert.Empty(Directory.GetFiles(_cacheDirectory));
    }

    [Fact]
    public async Task Images_are_released_when_the_card_cannot_be_shown()
    {
        var receiver = new CardReceiver(
            new ImageCache(_cacheDirectory),
            _ui,
            new FixedTimeProvider(Now, Tokyo),
            onCard: _ => throw new InvalidOperationException("The window is gone."),
            onShow: () => { },
            onQuit: () => { });

        await Assert.ThrowsAsync<InvalidOperationException>(() => receiver.HandleAsync(Post(_temp.WriteFile("one.png", "1"))));

        Assert.Empty(Directory.GetFiles(_cacheDirectory));
    }

    private CardReceiver Receiver(IImageStore? images = null) => new(
        images ?? new ImageCache(_cacheDirectory),
        _ui,
        new FixedTimeProvider(Now, Tokyo),
        onCard: _cards.Add,
        onShow: () => _shown++,
        onQuit: () => _quit++);

    private static ViewerMessage Post(params string[] paths) =>
        new() { Command = ViewerMessage.Post, Session = "s-1", Group = "g-1", Paths = [.. paths] };

    private sealed class NeverCompletingUiDispatcher : IUiDispatcher
    {
        private readonly TaskCompletionSource _never = new();

        public int Invocations { get; private set; }

        public Task InvokeAsync(Action action)
        {
            Invocations++;
            return _never.Task;
        }
    }

    /// <summary>Imports through <paramref name="inner"/> but throws for one file, like an unexpected I/O error.</summary>
    private sealed class FailingStore(IImageStore inner, string failOn) : IImageStore
    {
        public CardImage? Import(string sourcePath) =>
            Path.GetFileName(sourcePath) == failOn ? throw new InvalidOperationException("Unexpected.") : inner.Import(sourcePath);

        public void Release(IEnumerable<CardImage> images) => inner.Release(images);
    }
}
