using System.Globalization;
using AgentImageViewer.Core.Cards;
using AgentImageViewer.Core.Images;
using AgentImageViewer.Core.Tests.TestSupport;

namespace AgentImageViewer.Core.Tests.Cards;

public sealed class ResponseCardTests
{
    private static readonly DateTime Noon = new(2026, 1, 2, 12, 0, 0);

    [Theory]
    [InlineData("My session", "/home/me/project", "My session")]
    [InlineData("  padded  ", null, "padded")]
    [InlineData(null, "/home/me/project", "project")]
    [InlineData("", "/home/me/project", "project")]
    [InlineData("   ", "/home/me/project/", "project")]
    [InlineData(null, @"C:\Users\me\source\viewer", "viewer")]
    [InlineData(null, @"C:\Users\me\source\viewer\", "viewer")]
    [InlineData(null, @"\\wsl.localhost\Ubuntu\home\me\project", "project")]
    [InlineData(null, "project", "project")]
    [InlineData(null, "my project.v2", "my project.v2")]
    [InlineData(null, null, "Claude Code")]
    [InlineData(null, "", "Claude Code")]
    [InlineData(null, "  ", "Claude Code")]
    [InlineData(null, "/", "/")]
    [InlineData(null, @"C:\", @"C:\")]
    [InlineData(null, "C:", "C:")]
    public void The_title_is_the_session_name_or_else_the_project_folder(string? sessionName, string? cwd, string expected)
    {
        var card = new ResponseCard("s", "g", sessionName, cwd, "text", [], Noon);

        Assert.Equal(expected, card.Title);
    }

    [Theory]
    [InlineData(null, "", false, "")]
    [InlineData("", "", false, "")]
    [InlineData(" \r\n ", "", false, "")]
    [InlineData("  Hello  ", "Hello", true, "Hello")]
    [InlineData("first\nsecond", "first\nsecond", true, "first second")]
    [InlineData("first\r\n\r\n  second  \r\nthird\n", "first\r\n\r\n  second  \r\nthird", true, "first second third")]
    public void The_text_is_trimmed_and_collapsed_to_one_line_for_the_card(string? text, string expectedText, bool hasText, string singleLine)
    {
        var card = new ResponseCard("s", "g", null, null, text, [], Noon);

        Assert.Equal(expectedText, card.Text);
        Assert.Equal(hasText, card.HasText);
        Assert.Equal(singleLine, card.SingleLineText);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void A_blank_group_means_no_group(string? group)
    {
        Assert.Null(new ResponseCard("s", group, null, null, null, [], Noon).Group);
    }

    [Theory]
    [InlineData("s", "g", "s", "g", true)]
    [InlineData("s", "g", "other", "g", false)]
    [InlineData("s", "g", "s", "other", false)]
    [InlineData("s", null, "s", null, false)]
    [InlineData("s", "g", "s", null, false)]
    [InlineData("s", null, "s", "g", false)]
    public void Cards_merge_only_within_the_same_session_and_group(string session, string? group, string newerSession, string? newerGroup, bool expected)
    {
        var card = new ResponseCard(session, group, null, null, "text", [], Noon);
        var newer = new ResponseCard(newerSession, newerGroup, null, null, "other text", [], Noon);

        Assert.Equal(expected, card.CanMerge(newer));
    }

    [Fact]
    public void Merge_appends_new_images_and_returns_the_ones_already_on_the_card()
    {
        var a = RecordingImageStore.Image("a");
        var b = RecordingImageStore.Image("b");
        var bAgain = RecordingImageStore.Image("b-again", hash: "b");
        var c = RecordingImageStore.Image("c");
        var card = new ResponseCard("s", "g", null, null, "text", [a, b], Noon);
        var newer = new ResponseCard("s", "g", null, null, "text", [bAgain, c], Noon.AddSeconds(5));

        var duplicates = card.Merge(newer);

        Assert.Equal([a, b, c], card.Images);
        Assert.Equal([bAgain], duplicates);
    }

    [Fact]
    public void Merge_takes_over_the_newer_time_and_announces_it()
    {
        var card = new ResponseCard("s", "g", null, null, "text", [], Noon);
        var newer = new ResponseCard("s", "g", null, null, "text", [], Noon.AddSeconds(5));
        var changed = new List<string?>();
        card.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        card.Merge(newer);

        Assert.Equal(Noon.AddSeconds(5), card.ReceivedAt);
        Assert.Equal([nameof(ResponseCard.ReceivedAt), nameof(ResponseCard.TimeLabel)], changed);
    }

    [Fact]
    public void Merge_keeps_the_original_title_and_text()
    {
        var card = new ResponseCard("s", "g", "Original", null, "original text", [], Noon);

        card.Merge(new ResponseCard("s", "g", "Renamed", null, "newer text", [], Noon));

        Assert.Equal("Original", card.Title);
        Assert.Equal("original text", card.Text);
    }

    [Fact]
    public void The_time_label_shows_hours_minutes_and_seconds_on_a_24_hour_clock()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var card = new ResponseCard("s", null, null, null, null, [], new DateTime(2026, 1, 2, 15, 4, 5));

            Assert.Equal("15:04:05", card.TimeLabel);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void An_image_is_named_after_its_source_file()
    {
        var image = new CardImage(Path.Combine("shots", "Screenshot 1.png"), Path.Combine("cache", "0123.png"), "hash");

        Assert.Equal("Screenshot 1.png", image.FileName);
    }
}
