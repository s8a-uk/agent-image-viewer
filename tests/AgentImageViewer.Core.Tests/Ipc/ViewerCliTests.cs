using AgentImageViewer.Core.Ipc;
using AgentImageViewer.Core.Tests.TestSupport;

namespace AgentImageViewer.Core.Tests.Ipc;

public sealed class ViewerCliTests : IDisposable
{
    private readonly string _pipeName = $"civ-test-{Guid.NewGuid():N}";
    private readonly RecordingHandler _handler = new();
    private readonly FakeLauncher _launcher = new();
    private readonly StringWriter _error = new();
    private readonly PipeServer _server;
    private readonly PipeClient _client;

    public ViewerCliTests()
    {
        _server = new PipeServer(_pipeName, _handler);
        _client = new PipeClient(_pipeName, _launcher, _error, new PipeClientTimeouts { QuickConnect = TimeSpan.FromSeconds(20) });
    }

    public void Dispose() => _server.Dispose();

    [Fact]
    public void A_valid_post_is_sent_and_exits_with_0()
    {
        _server.Start();

        var exitCode = ViewerCli.Run(["post", "--session", "s-1", "--group", "g-1", "one.png"], _client, _error);

        Assert.Equal(0, exitCode);
        var message = Assert.Single(_handler.Messages);
        Assert.Equal("post", message.Command);
        Assert.Equal("s-1", message.Session);
        Assert.Equal("g-1", message.Group);
        Assert.Equal([Path.GetFullPath("one.png")], message.Paths);
        Assert.Equal("", _error.ToString());
    }

    [Fact]
    public void Show_is_sent_and_exits_with_0()
    {
        _server.Start();

        Assert.Equal(0, ViewerCli.Run(["show"], _client, _error));

        Assert.Equal("show", Assert.Single(_handler.Messages).Command);
    }

    [Fact]
    public void Quit_exits_with_0_when_no_viewer_is_running()
    {
        Assert.Equal(0, ViewerCli.Run(["quit"], _client, _error));

        Assert.Equal(0, _launcher.LaunchCount);
    }

    [Fact]
    public void Quit_is_sent_to_a_running_viewer()
    {
        _server.Start();
        _launcher.Running = () => _handler.Messages.Count == 0;

        Assert.Equal(0, ViewerCli.Run(["quit"], _client, _error));

        Assert.Equal("quit", Assert.Single(_handler.Messages).Command);
    }

    [Fact]
    public void A_failed_send_exits_with_1()
    {
        var exitCode = ViewerCli.Run(["show"], new PipeClient(_pipeName, _launcher, _error, new PipeClientTimeouts { QuickConnect = TimeSpan.FromMilliseconds(100) }), _error);

        Assert.Equal(1, exitCode);
    }

    [Theory]
    [InlineData("frobnicate")]
    [InlineData("post")]
    [InlineData("post", "--session", "s")]
    [InlineData("post", "--bogus", "one.png")]
    public void A_usage_error_exits_with_2_and_prints_the_reason_and_the_usage(params string[] args)
    {
        _server.Start();

        var exitCode = ViewerCli.Run(args, _client, _error);

        Assert.Equal(2, exitCode);
        Assert.Empty(_handler.Messages);
        Assert.False(ViewerCommandLine.TryParse(args, out _, out var reason));
        Assert.Equal($"{reason}{Environment.NewLine}{ViewerCommandLine.Usage}{Environment.NewLine}", _error.ToString());
    }
}
