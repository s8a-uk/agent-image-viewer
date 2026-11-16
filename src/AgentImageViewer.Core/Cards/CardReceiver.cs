using AgentImageViewer.Core.Images;
using AgentImageViewer.Core.Ipc;

namespace AgentImageViewer.Core.Cards;

/// <summary>Turns each "post" into a card right away; "show" and "quit" control the app.</summary>
public sealed class CardReceiver : IViewerMessageHandler
{
    /// <summary>Images taken from one post; the rest are ignored. An agent reads one image at a time.</summary>
    public const int MaxImagesPerPost = 32;

    private readonly IImageStore _images;
    private readonly IUiDispatcher _ui;
    private readonly TimeProvider _time;
    private readonly Action<ResponseCard> _onCard;
    private readonly Action _onShow;
    private readonly Action _onQuit;

    /// <summary>The callbacks are invoked on the UI thread.</summary>
    public CardReceiver(IImageStore images, IUiDispatcher ui, TimeProvider time, Action<ResponseCard> onCard, Action onShow, Action onQuit)
    {
        _images = images;
        _ui = ui;
        _time = time;
        _onCard = onCard;
        _onShow = onShow;
        _onQuit = onQuit;
    }

    private async Task PostAsync(ViewerMessage message, string session, IEnumerable<string> paths)
    {
        var imported = new List<CardImage>();
        var shown = false;
        try
        {
            foreach (var path in paths.Take(MaxImagesPerPost))
            {
                if (_images.Import(path) is { } image)
                {
                    imported.Add(image);
                }
            }

            var unique = imported.DistinctBy(i => i.Hash).ToList();
            if (unique.Count > 0)
            {
                var card = new ResponseCard(session, message.Group, message.SessionName, message.Cwd, message.Text, unique, _time.GetLocalNow().DateTime);
                await _ui.InvokeAsync(() => _onCard(card));
                _images.Release(imported.Except(unique));
                shown = true;
            }
        }
        finally
        {
            // Whatever did not end up on a card (duplicates aside) must not stay in the cache until the next start.
            if (!shown)
            {
                _images.Release(imported);
            }
        }
    }

    public async Task HandleAsync(ViewerMessage message)
    {
        switch (message.Command)
        {
            case ViewerMessage.Post when message is { Session: { } session, Paths: { } paths }:
                // Import here, off the UI thread. The same picture posted twice is shown once;
                // a re-captured screenshot at the same path has a different hash and is kept.
                await PostAsync(message, session, paths);
                break;

            case ViewerMessage.Show:
                await _ui.InvokeAsync(_onShow);
                break;

            case ViewerMessage.Quit:
                // Not awaited: the pipe server must still send "ok" before the app shuts down.
                _ = _ui.InvokeAsync(_onQuit);
                break;
        }
    }
}
