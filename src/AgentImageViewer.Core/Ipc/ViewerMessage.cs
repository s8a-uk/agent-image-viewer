namespace AgentImageViewer.Core.Ipc;

/// <summary>One line of JSON sent over the pipe.</summary>
public sealed class ViewerMessage
{
    public const string Post = "post";
    public const string Show = "show";
    public const string Quit = "quit";

    public string Command { get; set; } = "";

    public string? Session { get; set; }

    /// <summary>Id of the agent message the images belong to; cards of one session merge only within a group.</summary>
    public string? Group { get; set; }

    /// <summary>Display name of the Claude Code session; shown as the card title.</summary>
    public string? SessionName { get; set; }

    public string? Cwd { get; set; }

    public string? Text { get; set; }

    public List<string>? Paths { get; set; }
}
