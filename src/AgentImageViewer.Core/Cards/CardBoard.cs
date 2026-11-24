using System.Collections.ObjectModel;
using AgentImageViewer.Core.Images;

namespace AgentImageViewer.Core.Cards;

/// <summary>
/// The cards on display, oldest first. Owns the rules for merging, trimming and removing cards,
/// and releases the cached images of everything that leaves the board. Not thread-safe: use it from the UI thread.
/// </summary>
public sealed class CardBoard
{
    private readonly ObservableCollection<ResponseCard> _cards = [];
    private readonly IImageStore _images;
    private int _maxCards;

    public CardBoard(IImageStore images, int maxCards)
    {
        _images = images;
        _maxCards = maxCards;
        Cards = new ReadOnlyObservableCollection<ResponseCard>(_cards);
    }

    public ReadOnlyObservableCollection<ResponseCard> Cards { get; }

    /// <summary>Lowering the limit drops the oldest cards at once.</summary>
    public int MaxCards
    {
        get => _maxCards;
        set
        {
            _maxCards = value;
            Trim();
        }
    }

    public void Add(ResponseCard card)
    {
        // Only the session's newest card is a merge candidate, even if other sessions' cards came after it.
        // Merging keeps the card where it is.
        var latest = _cards.LastOrDefault(c => c.Session == card.Session);
        if (latest is not null && latest.CanMerge(card))
        {
            _images.Release(latest.Merge(card));
        }
        else
        {
            _cards.Add(card);
            Trim();
        }
    }

    public void Remove(ResponseCard card)
    {
        if (_cards.Remove(card))
        {
            _images.Release(card.Images);
        }
    }

    public void Clear()
    {
        foreach (var card in _cards)
        {
            _images.Release(card.Images);
        }

        _cards.Clear();
    }

    private void Trim()
    {
        while (_cards.Count > _maxCards)
        {
            _images.Release(_cards[0].Images);
            _cards.RemoveAt(0);
        }
    }
}
