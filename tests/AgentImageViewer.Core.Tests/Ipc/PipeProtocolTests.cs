using System.Text.Json;
using AgentImageViewer.Core.Ipc;

namespace AgentImageViewer.Core.Tests.Ipc;

/// <summary>Pins the wire format: the hook plugin and older viewers in the field speak exactly this.</summary>
public sealed class PipeProtocolTests
{
    [Fact]
    public void A_full_post_serializes_to_camel_case_properties_in_declaration_order()
    {
        var message = new ViewerMessage
        {
            Command = ViewerMessage.Post,
            Session = "s-1",
            Group = "g-1",
            SessionName = "My session",
            Cwd = "/home/me/project",
            Text = "Hello",
            Paths = ["C:\\tmp\\one.png", "C:\\tmp\\two.png"],
        };

        Assert.Equal(
            """{"command":"post","session":"s-1","group":"g-1","sessionName":"My session","cwd":"/home/me/project","text":"Hello","paths":["C:\\tmp\\one.png","C:\\tmp\\two.png"]}""",
            PipeProtocol.Serialize(message));
    }

    [Theory]
    [InlineData(ViewerMessage.Show, """{"command":"show"}""")]
    [InlineData(ViewerMessage.Quit, """{"command":"quit"}""")]
    public void Unset_properties_are_omitted(string command, string expected)
    {
        Assert.Equal(expected, PipeProtocol.Serialize(new ViewerMessage { Command = command }));
    }

    [Fact]
    public void A_message_is_always_a_single_line()
    {
        var message = new ViewerMessage { Command = ViewerMessage.Post, Text = "first\r\nsecond\n日本語" };

        var line = PipeProtocol.Serialize(message);

        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
        Assert.Equal("first\r\nsecond\n日本語", PipeProtocol.Deserialize(line)!.Text);
    }

    [Fact]
    public void Deserialize_reads_the_documented_format()
    {
        var message = PipeProtocol.Deserialize(
            """{"command":"post","session":"s-1","group":"g-1","sessionName":"n","cwd":"c","text":"t","paths":["a","b"]}""");

        Assert.NotNull(message);
        Assert.Equal("post", message.Command);
        Assert.Equal("s-1", message.Session);
        Assert.Equal("g-1", message.Group);
        Assert.Equal("n", message.SessionName);
        Assert.Equal("c", message.Cwd);
        Assert.Equal("t", message.Text);
        Assert.Equal(["a", "b"], message.Paths);
    }

    [Fact]
    public void Deserialize_ignores_properties_it_does_not_know()
    {
        var message = PipeProtocol.Deserialize("""{"command":"show","addedLater":42}""");

        Assert.Equal("show", message!.Command);
    }

    [Fact]
    public void Deserialize_leaves_missing_properties_unset()
    {
        var message = PipeProtocol.Deserialize("{}");

        Assert.Equal("", message!.Command);
        Assert.Null(message.Session);
        Assert.Null(message.Paths);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"command":""")]
    [InlineData("""{"paths":"not a list"}""")]
    public void Deserialize_throws_for_a_malformed_line(string line)
    {
        Assert.Throws<JsonException>(() => PipeProtocol.Deserialize(line));
    }

    [Fact]
    public void The_command_names_and_acknowledgement_are_fixed()
    {
        Assert.Equal("post", ViewerMessage.Post);
        Assert.Equal("show", ViewerMessage.Show);
        Assert.Equal("quit", ViewerMessage.Quit);
        Assert.Equal("ok", PipeProtocol.OkResponse);
    }

    [Fact]
    public void The_pipe_and_mutex_are_named_after_the_user_and_session()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();

        // The mutex is per session through the Local\ namespace; the pipe namespace is machine-wide.
        Assert.Equal($"AgentImageViewer-{Environment.UserName}-{process.SessionId}", PipeProtocol.PipeName);
        Assert.Equal($@"Local\AgentImageViewer-{Environment.UserName}", PipeProtocol.MutexName);
    }

    [Fact]
    public void The_exit_codes_are_fixed()
    {
        Assert.Equal(0, ExitCode.Ok);
        Assert.Equal(1, ExitCode.Failure);
        Assert.Equal(2, ExitCode.Usage);
    }
}
