using System.Collections.Specialized;
using AgentImageViewer.Core.Cards;
using AgentImageViewer.Core.Tests.TestSupport;

namespace AgentImageViewer.Core.Tests.Cards;

public sealed class CardBoardTests
{
    private static readonly DateTime Noon = new(2026, 1, 2, 12, 0, 0);

    private readonly RecordingImageStore _images = new();

    [Fact]
    public void Cards_are_listed_oldest_first()
    {
        var board = new CardBoard(_images, maxCards: 5);
        var first = Card("s1", "g1", "a");
        var second = Card("s2", "g1", "b");
        var third = Card("s1", "g2", "c");

        board.Add(first);
        board.Add(second);
        board.Add(third);

        Assert.Equal([first, second, third], board.Cards);
        Assert.Empty(_images.Released);
    }

    [Fact]
    public void A_post_for_the_same_message_is_merged_into_the_existing_card()
    {
        var board = new CardBoard(_images, maxCards: 5);
        var card = Card("s1", "g1", "a");
        board.Add(card);

        board.Add(Card("s1", "g1", "b", receivedAt: Noon.AddSeconds(3)));

        Assert.Same(card, Assert.Single(board.Cards));
        Assert.Equal(["a", "b"], card.Images.Select(i => i.Hash));
        Assert.Equal(Noon.AddSeconds(3), card.ReceivedAt);
        Assert.Empty(_images.Released);
    }

    [Fact]
    public void A_merged_card_stays_where_it_is_even_when_other_sessions_posted_after_it()
    {
        var board = new CardBoard(_images, maxCards: 5);
        var mine = Card("s1", "g1", "a");
        var theirs = Card("s2", "g1", "b");
        board.Add(mine);
        board.Add(theirs);

        board.Add(Card("s1", "g1", "c"));

        Assert.Equal([mine, theirs], board.Cards);
        Assert.Equal(["a", "c"], mine.Images.Select(i => i.Hash));
    }

    [Fact]
    public void Merging_releases_the_images_the_card_already_shows()
    {
        var board = new CardBoard(_images, maxCards: 5);
        board.Add(Card("s1", "g1", "a", "b"));
        var newer = Card("s1", "g1", "b", "c");
        var duplicate = newer.Images[0];

        board.Add(newer);

        Assert.Equal([duplicate], _images.Released);
        Assert.Equal(["a", "b", "c"], board.Cards[0].Images.Select(i => i.Hash));
    }

    [Fact]
    public void Only_the_sessions_newest_card_is_a_merge_candidate()
    {
        var board = new CardBoard(_images, maxCards: 5);
        var older = Card("s1", "g1", "a");
        var newest = Card("s1", "g2", "b");
        board.Add(older);
        board.Add(newest);

        var backToFirstGroup = Card("s1", "g1", "c");
        board.Add(backToFirstGroup);

        Assert.Equal([older, newest, backToFirstGroup], board.Cards);
        Assert.Equal(["a"], older.Images.Select(i => i.Hash));
    }

    [Fact]
    public void Cards_without_a_group_never_merge()
    {
        var board = new CardBoard(_images, maxCards: 5);

        board.Add(Card("s1", null, "a"));
        board.Add(Card("s1", null, "a"));

        Assert.Equal(2, board.Cards.Count);
        Assert.Empty(_images.Released);
    }

    [Fact]
    public void The_same_group_id_in_another_session_is_a_different_message()
    {
        var board = new CardBoard(_images, maxCards: 5);

        board.Add(Card("s1", "g1", "a"));
        board.Add(Card("s2", "g1", "b"));

        Assert.Equal(2, board.Cards.Count);
    }

    [Fact]
    public void Going_over_the_limit_drops_the_oldest_card_and_releases_its_images()
    {
        var board = new CardBoard(_images, maxCards: 2);
        var first = Card("s1", "g1", "a", "b");
        var second = Card("s1", "g2", "c");
        var third = Card("s1", "g3", "d");
        board.Add(first);
        board.Add(second);

        board.Add(third);

        Assert.Equal([second, third], board.Cards);
        Assert.Equal(first.Images, _images.Released);
    }

    [Fact]
    public void Merging_does_not_count_towards_the_limit()
    {
        var board = new CardBoard(_images, maxCards: 2);
        var first = Card("s1", "g1", "a");
        var second = Card("s2", "g1", "b");
        board.Add(first);
        board.Add(second);

        board.Add(Card("s2", "g1", "c"));
        board.Add(Card("s1", "g1", "d"));

        Assert.Equal([first, second], board.Cards);
        Assert.Empty(_images.Released);
    }

    [Fact]
    public void Lowering_the_limit_drops_the_oldest_cards_at_once()
    {
        var board = new CardBoard(_images, maxCards: 5);
        var cards = Enumerable.Range(0, 4).Select(i => Card("s1", $"g{i}", $"image{i}")).ToList();
        cards.ForEach(board.Add);

        board.MaxCards = 1;

        Assert.Equal(1, board.MaxCards);
        Assert.Equal([cards[3]], board.Cards);
        Assert.Equal(cards.Take(3).SelectMany(c => c.Images), _images.Released);
    }

    [Fact]
    public void Raising_the_limit_keeps_every_card()
    {
        var board = new CardBoard(_images, maxCards: 2);
        board.Add(Card("s1", "g1", "a"));
        board.Add(Card("s1", "g2", "b"));

        board.MaxCards = 10;
        board.Add(Card("s1", "g3", "c"));

        Assert.Equal(3, board.Cards.Count);
        Assert.Empty(_images.Released);
    }

    [Fact]
    public void Remove_takes_the_card_off_the_board_and_releases_its_images()
    {
        var board = new CardBoard(_images, maxCards: 5);
        var kept = Card("s1", "g1", "a");
        var removed = Card("s1", "g2", "b", "c");
        board.Add(kept);
        board.Add(removed);

        board.Remove(removed);

        Assert.Equal([kept], board.Cards);
        Assert.Equal(removed.Images, _images.Released);
    }

    [Fact]
    public void Removing_a_card_that_is_not_on_the_board_releases_nothing()
    {
        var board = new CardBoard(_images, maxCards: 5);
        board.Add(Card("s1", "g1", "a"));

        board.Remove(Card("s1", "g2", "b"));

        Assert.Single(board.Cards);
        Assert.Empty(_images.Released);
    }

    [Fact]
    public void After_a_card_is_removed_the_session_merges_into_its_remaining_newest_card()
    {
        var board = new CardBoard(_images, maxCards: 5);
        var older = Card("s1", "g1", "a");
        var newest = Card("s1", "g2", "b");
        board.Add(older);
        board.Add(newest);
        board.Remove(newest);

        board.Add(Card("s1", "g1", "c"));

        Assert.Same(older, Assert.Single(board.Cards));
        Assert.Equal(["a", "c"], older.Images.Select(i => i.Hash));
    }

    [Fact]
    public void Clear_empties_the_board_and_releases_every_image()
    {
        var board = new CardBoard(_images, maxCards: 5);
        var first = Card("s1", "g1", "a", "b");
        var second = Card("s2", "g1", "c");
        board.Add(first);
        board.Add(second);

        board.Clear();

        Assert.Empty(board.Cards);
        Assert.Equal(first.Images.Concat(second.Images), _images.Released);
    }

    [Fact]
    public void The_card_list_announces_its_changes()
    {
        var board = new CardBoard(_images, maxCards: 1);
        var actions = new List<NotifyCollectionChangedAction>();
        ((INotifyCollectionChanged)board.Cards).CollectionChanged += (_, e) => actions.Add(e.Action);
        var card = Card("s1", "g1", "a");

        board.Add(card);
        board.Add(Card("s1", "g1", "b"));
        board.Add(Card("s1", "g2", "c"));
        board.Remove(board.Cards[0]);
        board.Clear();

        Assert.Equal(
            [
                NotifyCollectionChangedAction.Add,
                NotifyCollectionChangedAction.Add,
                NotifyCollectionChangedAction.Remove,
                NotifyCollectionChangedAction.Remove,
                NotifyCollectionChangedAction.Reset,
            ],
            actions);
    }

    private static ResponseCard Card(string session, string? group, params string[] imageHashes) =>
        Card(session, group, imageHashes, Noon);

    private static ResponseCard Card(string session, string? group, string imageHash, DateTime receivedAt) =>
        Card(session, group, [imageHash], receivedAt);

    private static ResponseCard Card(string session, string? group, IEnumerable<string> imageHashes, DateTime receivedAt)
    {
        var images = imageHashes.Select(hash => RecordingImageStore.Image(hash));
        return new ResponseCard(session, group, null, null, "text", images, receivedAt);
    }
}
