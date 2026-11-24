using AgentImageViewer.Core.Ipc;
using AgentImageViewer.Core.Tests.TestSupport;

namespace AgentImageViewer.Core.Tests.Ipc;

public sealed class ViewerCommandLineTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Post_with_every_option_fills_the_message()
    {
        var textFile = _temp.WriteFile("message.txt", "Here is the screenshot.\nSecond line.");
        var first = _temp.File("one.png");
        var second = _temp.File("two.jpg");

        var parsed = ViewerCommandLine.TryParse(
            ["post", "--session", "s-1", "--group", "g-1", "--session-name", "My session", "--cwd", "/home/me/project", "--text-file", textFile, first, second],
            out var message,
            out var error);

        Assert.True(parsed);
        Assert.Null(error);
        Assert.NotNull(message);
        Assert.Equal("post", message.Command);
        Assert.Equal("s-1", message.Session);
        Assert.Equal("g-1", message.Group);
        Assert.Equal("My session", message.SessionName);
        Assert.Equal("/home/me/project", message.Cwd);
        Assert.Equal("Here is the screenshot.\nSecond line.", message.Text);
        Assert.Equal([first, second], message.Paths);
    }

    [Fact]
    public void Post_needs_only_a_session_and_one_path()
    {
        var path = _temp.File("one.png");

        Assert.True(ViewerCommandLine.TryParse(["post", "--session", "s-1", path], out var message, out _));

        Assert.Equal("s-1", message!.Session);
        Assert.Null(message.Group);
        Assert.Null(message.SessionName);
        Assert.Null(message.Cwd);
        Assert.Null(message.Text);
        Assert.Equal([path], message.Paths);
    }

    [Fact]
    public void Options_and_paths_may_come_in_any_order()
    {
        var first = _temp.File("one.png");
        var second = _temp.File("two.png");

        Assert.True(ViewerCommandLine.TryParse(["post", first, "--group", "g", second, "--session", "s"], out var message, out _));

        Assert.Equal("s", message!.Session);
        Assert.Equal("g", message.Group);
        Assert.Equal([first, second], message.Paths);
    }

    [Fact]
    public void Relative_paths_are_resolved_against_the_current_directory()
    {
        Assert.True(ViewerCommandLine.TryParse(["post", "--session", "s", "shots/one.png"], out var message, out _));

        var path = Assert.Single(message!.Paths!);
        Assert.True(Path.IsPathFullyQualified(path));
        Assert.Equal(Path.GetFullPath("shots/one.png"), path);
    }

    [Theory]
    [InlineData("show")]
    [InlineData("quit")]
    public void Show_and_quit_take_no_arguments(string command)
    {
        Assert.True(ViewerCommandLine.TryParse([command], out var message, out var error));

        Assert.Null(error);
        Assert.Equal(command, message!.Command);
        Assert.Null(message.Session);
        Assert.Null(message.Paths);
    }

    [Theory]
    [InlineData("POST", "post")]
    [InlineData("Show", "show")]
    [InlineData("QuIt", "quit")]
    public void The_command_is_case_insensitive(string typed, string expected)
    {
        Assert.True(ViewerCommandLine.TryParse([typed, "--session", "s", "one.png"], out var message, out _));

        Assert.Equal(expected, message!.Command);
    }

    [Fact]
    public void An_unknown_command_is_rejected()
    {
        Assert.False(ViewerCommandLine.TryParse(["add", "one.png"], out var message, out var error));

        Assert.Null(message);
        Assert.Equal("Unknown command: add", error);
    }

    [Fact]
    public void No_arguments_at_all_is_rejected()
    {
        Assert.False(ViewerCommandLine.TryParse([], out var message, out var error));

        Assert.Null(message);
        Assert.Equal("No command given", error);
    }

    [Fact]
    public void An_unknown_option_is_rejected()
    {
        Assert.False(ViewerCommandLine.TryParse(["post", "--session", "s", "--verbose", "one.png"], out _, out var error));

        Assert.Equal("Unknown or incomplete option: --verbose", error);
    }

    [Theory]
    [InlineData("--session")]
    [InlineData("--group")]
    [InlineData("--session-name")]
    [InlineData("--cwd")]
    [InlineData("--text-file")]
    public void An_option_without_its_value_is_rejected(string option)
    {
        Assert.False(ViewerCommandLine.TryParse(["post", "one.png", option], out _, out var error));

        Assert.Equal($"Unknown or incomplete option: {option}", error);
    }

    [Fact]
    public void Options_are_case_sensitive()
    {
        Assert.False(ViewerCommandLine.TryParse(["post", "--Session", "s", "one.png"], out _, out var error));

        Assert.Equal("Unknown or incomplete option: --Session", error);
    }

    [Theory]
    [InlineData]
    [InlineData("--session", "")]
    public void Post_without_a_session_is_rejected(params string[] sessionArgs)
    {
        Assert.False(ViewerCommandLine.TryParse(["post", .. sessionArgs, "one.png"], out _, out var error));

        Assert.Equal("--session is required", error);
    }

    [Fact]
    public void Post_without_a_path_is_rejected()
    {
        Assert.False(ViewerCommandLine.TryParse(["post", "--session", "s"], out _, out var error));

        Assert.Equal("No image path given", error);
    }

    [Fact]
    public void An_unreadable_text_file_only_drops_the_text()
    {
        Assert.True(ViewerCommandLine.TryParse(
            ["post", "--session", "s", "--text-file", _temp.File("missing.txt"), "one.png"],
            out var message,
            out _));

        Assert.Null(message!.Text);
    }

    [Fact]
    public void The_text_file_is_read_as_utf8()
    {
        var textFile = _temp.WriteFile("message.txt", "スクリーンショットです ✓");

        Assert.True(ViewerCommandLine.TryParse(["post", "--session", "s", "--text-file", textFile, "one.png"], out var message, out _));

        Assert.Equal("スクリーンショットです ✓", message!.Text);
    }

    [Fact]
    public void Usage_documents_the_three_commands()
    {
        var lines = ViewerCommandLine.Usage.ReplaceLineEndings("\n").Split('\n');

        Assert.Equal(
            [
                "Usage:",
                "  AgentImageViewer.exe post --session <id> [--group <id>] [--session-name <name>] [--cwd <dir>] [--text-file <path>] <image path>...",
                "  AgentImageViewer.exe show",
                "  AgentImageViewer.exe quit",
            ],
            lines);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\0path.png")]
    public void An_empty_or_invalid_image_path_is_a_usage_error(string path)
    {
        Assert.False(ViewerCommandLine.TryParse(["post", "--session", "s", "one.png", path], out var message, out var error));

        Assert.Null(message);
        Assert.Equal($"Invalid image path: \"{path}\"", error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad\0name.txt")]
    public void An_unusable_text_file_path_leaves_the_card_without_text(string textFile)
    {
        Assert.True(ViewerCommandLine.TryParse(["post", "--session", "s", "--text-file", textFile, "one.png"], out var message, out _));

        Assert.Null(message!.Text);
    }
}
