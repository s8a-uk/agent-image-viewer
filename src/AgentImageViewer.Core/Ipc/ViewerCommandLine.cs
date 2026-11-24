using System.Diagnostics.CodeAnalysis;

namespace AgentImageViewer.Core.Ipc;

/// <summary>Parses the client command line into a <see cref="ViewerMessage"/>.</summary>
public static class ViewerCommandLine
{
    public const string Usage = """
        Usage:
          AgentImageViewer.exe post --session <id> [--group <id>] [--session-name <name>] [--cwd <dir>] [--text-file <path>] <image path>...
          AgentImageViewer.exe show
          AgentImageViewer.exe quit
        """;

    public static bool TryParse(
        IReadOnlyList<string> args,
        [NotNullWhen(true)] out ViewerMessage? message,
        [NotNullWhen(false)] out string? error)
    {
        message = null;
        error = null;

        if (args.Count == 0)
        {
            error = "No command given";
            return false;
        }

        var command = args[0].ToLowerInvariant();
        if (command is not (ViewerMessage.Post or ViewerMessage.Show or ViewerMessage.Quit))
        {
            error = $"Unknown command: {args[0]}";
            return false;
        }

        var result = new ViewerMessage { Command = command };
        var paths = new List<string>();
        string? textFile = null;

        for (var i = 1; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--session" when i + 1 < args.Count:
                    result.Session = args[++i];
                    break;
                case "--group" when i + 1 < args.Count:
                    result.Group = args[++i];
                    break;
                case "--session-name" when i + 1 < args.Count:
                    result.SessionName = args[++i];
                    break;
                case "--cwd" when i + 1 < args.Count:
                    result.Cwd = args[++i];
                    break;
                case "--text-file" when i + 1 < args.Count:
                    textFile = args[++i];
                    break;
                case var option when option.StartsWith("--", StringComparison.Ordinal):
                    error = $"Unknown or incomplete option: {option}";
                    return false;
                default:
                    paths.Add(args[i]);
                    break;
            }
        }

        if (command != ViewerMessage.Post)
        {
            message = result;
            return true;
        }

        if (string.IsNullOrEmpty(result.Session))
        {
            error = "--session is required";
            return false;
        }

        if (paths.Count == 0)
        {
            error = "No image path given";
            return false;
        }

        // Resolve here so the resident process receives absolute paths.
        if (paths.FirstOrDefault(p => string.IsNullOrWhiteSpace(p) || p.Contains('\0', StringComparison.Ordinal)) is { } invalid)
        {
            error = $"Invalid image path: \"{invalid}\"";
            return false;
        }

        result.Paths = paths.Select(Path.GetFullPath).ToList();

        if (textFile is not null)
        {
            result.Text = TryReadText(textFile);
        }

        message = result;
        return true;
    }

    // Read now: the hook deletes the file as soon as this process exits.
    // A card without its text is still worth showing, so an unreadable file is not an error.
    private static string? TryReadText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
