using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using AgentImageViewer.Core.Images;

namespace AgentImageViewer.Core.Cards;

/// <summary>One agent message and the images it was written about.</summary>
public sealed class ResponseCard : INotifyPropertyChanged
{
    private const string DefaultTitle = "Claude Code";

    private DateTime _receivedAt;

    public ResponseCard(string session, string? group, string? sessionName, string? cwd, string? text, IEnumerable<CardImage> images, DateTime receivedAt)
    {
        Session = session;
        Group = string.IsNullOrWhiteSpace(group) ? null : group;
        Title = string.IsNullOrWhiteSpace(sessionName) ? ProjectName(cwd) : sessionName.Trim();
        Text = text?.Trim() ?? "";
        Images = new ObservableCollection<CardImage>(images);
        _receivedAt = receivedAt;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Session { get; }

    /// <summary>Id of the agent message these images belong to (sent by the hook); null when unknown.</summary>
    public string? Group { get; }

    /// <summary>Session name, or the project folder name when none was sent.</summary>
    public string Title { get; }

    public string Text { get; }

    public bool HasText => Text.Length > 0;

    /// <summary>The text collapsed to one line for the card; the full text is in the tooltip.</summary>
    public string SingleLineText => string.Join(' ', Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    public ObservableCollection<CardImage> Images { get; }

    public DateTime ReceivedAt
    {
        get => _receivedAt;
        private set
        {
            _receivedAt = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ReceivedAt)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TimeLabel)));
        }
    }

    public string TimeLabel => ReceivedAt.ToString("HH:mm:ss", CultureInfo.CurrentCulture);

    /// <summary>
    /// Images read under the same agent message (same session and group) belong on one card.
    /// Text is not compared: two messages can say the same thing. Cards without a group never merge.
    /// </summary>
    public bool CanMerge(ResponseCard newer) =>
        Session == newer.Session && Group is not null && Group == newer.Group;

    /// <summary>Appends the newer card's images and returns the duplicates the caller should release.</summary>
    public List<CardImage> Merge(ResponseCard newer)
    {
        var duplicates = new List<CardImage>();
        foreach (var image in newer.Images)
        {
            if (Images.Any(i => i.Hash == image.Hash))
            {
                duplicates.Add(image);
            }
            else
            {
                Images.Add(image);
            }
        }

        ReceivedAt = newer.ReceivedAt;
        return duplicates;
    }

    private static string ProjectName(string? cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd))
        {
            return DefaultTitle;
        }

        // Split by hand so that Windows and WSL paths give the same result on any OS.
        var trimmed = cwd.TrimEnd('/', '\\');
        var start = trimmed.LastIndexOfAny(['/', '\\']) + 1;
        if (start == 0 && trimmed.Length >= 2 && char.IsAsciiLetter(trimmed[0]) && trimmed[1] == ':')
        {
            // "C:" is a drive, not a folder name.
            start = 2;
        }

        return start == trimmed.Length ? cwd : trimmed[start..];
    }
}
