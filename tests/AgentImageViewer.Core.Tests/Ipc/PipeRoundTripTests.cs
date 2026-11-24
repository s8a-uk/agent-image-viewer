using System.ComponentModel;
using System.IO.Pipes;
using System.Text;
using AgentImageViewer.Core.Ipc;
using AgentImageViewer.Core.Tests.TestSupport;

namespace AgentImageViewer.Core.Tests.Ipc;

/// <summary>Runs the real pipe server and client against each other in-process, on a pipe of their own.</summary>
public sealed class PipeRoundTripTests : IDisposable
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    // Generous, so a slow machine never makes a wait expire that the test expects to succeed.
    private static readonly PipeClientTimeouts Timeouts = new()
    {
        QuickConnect = TimeSpan.FromSeconds(20),
        StartupConnect = TimeSpan.FromSeconds(20),
        StartupPollInterval = TimeSpan.FromMilliseconds(50),
        Response = TimeSpan.FromSeconds(20),
        Quit = TimeSpan.FromSeconds(20),
        QuitPollInterval = TimeSpan.FromMilliseconds(10),
    };

    // For tests in which no viewer is listening at first: do not sit out the whole first attempt.
    private static readonly PipeClientTimeouts NoViewerYet = Timeouts with { QuickConnect = TimeSpan.FromMilliseconds(100) };

    private readonly string _pipeName = $"civ-test-{Guid.NewGuid():N}";
    private readonly RecordingHandler _handler = new();
    private readonly FakeLauncher _launcher = new();
    private readonly StringWriter _error = new();
    private readonly List<PipeServer> _servers = [];

    public void Dispose()
    {
        foreach (var server in _servers)
        {
            server.Dispose();
        }
    }

    [Fact]
    public void Send_delivers_the_message_and_succeeds_on_the_acknowledgement()
    {
        StartServer();
        var message = new ViewerMessage
        {
            Command = ViewerMessage.Post,
            Session = "s-1",
            Group = "g-1",
            SessionName = "セッション",
            Cwd = "/work",
            Text = "line one\nline two",
            Paths = ["/tmp/one.png", "/tmp/two.png"],
        };

        var exitCode = Client().Send(message);

        Assert.Equal(ExitCode.Ok, exitCode);
        var received = Assert.Single(_handler.Messages);
        Assert.Equal(PipeProtocol.Serialize(message), PipeProtocol.Serialize(received));
        Assert.Equal(0, _launcher.LaunchCount);
        Assert.Equal("", _error.ToString());
    }

    [Fact]
    public void Messages_sent_one_after_another_are_handled_in_that_order()
    {
        StartServer();
        var client = Client();

        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(ExitCode.Ok, client.Send(new ViewerMessage { Command = ViewerMessage.Post, Session = $"{i}" }));
        }

        Assert.Equal(Enumerable.Range(0, 20).Select(i => $"{i}"), _handler.Messages.Select(m => m.Session));
    }

    [Fact]
    public async Task The_acknowledgement_is_sent_only_after_the_message_has_been_handled()
    {
        var gate = new TaskCompletionSource();
        _handler.Gate = gate.Task;
        StartServer();
        using var pipe = await ConnectRawAsync();
        using var reader = new StreamReader(pipe, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

        await writer.WriteLineAsync(PipeProtocol.Serialize(new ViewerMessage { Command = ViewerMessage.Show }));
        var response = reader.ReadLineAsync(TestContext.Current.CancellationToken).AsTask();
        await WaitUntilAsync(() => _handler.Messages.Count == 1);

        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.False(response.IsCompleted);

        gate.SetResult();
        Assert.Equal("ok", await response.WaitAsync(TestTimeout, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Several_clients_can_be_connected_at_once()
    {
        var gate = new TaskCompletionSource();
        _handler.Gate = gate.Task;
        StartServer();
        var client = Client();

        var sends = Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() => client.Send(new ViewerMessage { Command = ViewerMessage.Show })))
            .ToArray();
        await WaitUntilAsync(() => _handler.Messages.Count == sends.Length);
        gate.SetResult();

        Assert.All(await Task.WhenAll(sends).WaitAsync(TestTimeout, TestContext.Current.CancellationToken), code => Assert.Equal(ExitCode.Ok, code));
    }

    [Fact]
    public async Task A_malformed_line_gets_no_acknowledgement_and_does_not_stop_the_server()
    {
        StartServer();
        using (var pipe = await ConnectRawAsync())
        {
            using var reader = new StreamReader(pipe, leaveOpen: true);

            // Written straight to the pipe: a StreamWriter would flush again when disposed, which throws on
            // Windows once the server has closed the pipe.
            await pipe.WriteAsync("this is not json\n"u8.ToArray(), TestContext.Current.CancellationToken);

            // The server just closes the pipe. Linux reports that as end of stream, Windows as a broken pipe.
            string? response;
            try
            {
                response = await reader.ReadLineAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TestTimeout, TestContext.Current.CancellationToken);
            }
            catch (IOException)
            {
                response = null;
            }

            Assert.Null(response);
        }

        Assert.Empty(_handler.Messages);
        Assert.Equal(ExitCode.Ok, Client().Send(new ViewerMessage { Command = ViewerMessage.Show }));
    }

    [Fact]
    public void Send_fails_when_no_viewer_is_running_and_it_may_not_start_one()
    {
        var exitCode = Client(NoViewerYet).Send(new ViewerMessage { Command = ViewerMessage.Show }, startViewer: false);

        Assert.Equal(ExitCode.Failure, exitCode);
        Assert.Equal(0, _launcher.LaunchCount);
        Assert.Contains("AgentImageViewer is not running.", _error.ToString());
    }

    [Fact]
    public void Send_starts_the_viewer_when_none_is_running_and_waits_for_its_pipe()
    {
        var launched = new FakeLaunchedViewer();
        _launcher.OnLaunch = () =>
        {
            // Like the real viewer, the pipe opens a moment after the process has started.
            _ = Task.Delay(300).ContinueWith(_ => StartServer(), TaskScheduler.Default);
            return launched;
        };

        var exitCode = Client(NoViewerYet).Send(new ViewerMessage { Command = ViewerMessage.Post, Session = "s" });

        Assert.Equal(ExitCode.Ok, exitCode);
        Assert.Equal(1, _launcher.LaunchCount);
        Assert.True(launched.Disposed);
        Assert.Equal("s", Assert.Single(_handler.Messages).Session);
    }

    [Fact]
    public void Send_gives_up_at_once_when_the_viewer_cannot_be_started()
    {
        _launcher.OnLaunch = () => null;

        var exitCode = Client(NoViewerYet).Send(new ViewerMessage { Command = ViewerMessage.Show });

        Assert.Equal(ExitCode.Failure, exitCode);
        Assert.Equal(1, _launcher.LaunchCount);
        Assert.Contains("AgentImageViewer is not running.", _error.ToString());
    }

    [Fact]
    public void Send_gives_up_at_once_when_the_started_viewer_has_died()
    {
        _launcher.OnLaunch = () => new FakeLaunchedViewer { HasExited = true };

        var exitCode = Client(NoViewerYet).Send(new ViewerMessage { Command = ViewerMessage.Show });

        Assert.Equal(ExitCode.Failure, exitCode);
    }

    [Fact]
    public void Send_keeps_waiting_when_the_started_process_exited_because_another_viewer_is_starting()
    {
        _launcher.Running = () => true;
        _launcher.OnLaunch = () =>
        {
            _ = Task.Delay(300).ContinueWith(_ => StartServer(), TaskScheduler.Default);
            return new FakeLaunchedViewer { HasExited = true };
        };

        var exitCode = Client(NoViewerYet).Send(new ViewerMessage { Command = ViewerMessage.Show });

        Assert.Equal(ExitCode.Ok, exitCode);
    }

    [Fact]
    public void Send_gives_up_when_the_viewer_never_opens_its_pipe()
    {
        _launcher.OnLaunch = () => new FakeLaunchedViewer();

        var exitCode = Client(NoViewerYet with { StartupConnect = TimeSpan.FromMilliseconds(300) })
            .Send(new ViewerMessage { Command = ViewerMessage.Show });

        Assert.Equal(ExitCode.Failure, exitCode);
        Assert.Contains("AgentImageViewer is not running.", _error.ToString());
    }

    [Fact]
    public void Send_fails_when_starting_the_viewer_throws()
    {
        _launcher.OnLaunch = () => throw new Win32Exception("The system cannot find the file specified.");

        var exitCode = Client(NoViewerYet).Send(new ViewerMessage { Command = ViewerMessage.Show });

        Assert.Equal(ExitCode.Failure, exitCode);
        Assert.Contains("The system cannot find the file specified.", _error.ToString());
    }

    [Fact]
    public void Send_gives_up_when_the_viewer_does_not_answer()
    {
        var gate = new TaskCompletionSource();
        _handler.Gate = gate.Task;
        StartServer();

        var exitCode = Client(Timeouts with { Response = TimeSpan.FromMilliseconds(300) })
            .Send(new ViewerMessage { Command = ViewerMessage.Show });
        gate.SetResult();

        Assert.Equal(ExitCode.Failure, exitCode);
        Assert.Contains("AgentImageViewer did not respond in time.", _error.ToString());
    }

    [Fact]
    public async Task Send_fails_when_the_viewer_drops_the_connection_without_answering()
    {
        await using var viewer = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var dropped = viewer.WaitForConnectionAsync(TestContext.Current.CancellationToken).ContinueWith(_ => viewer.Dispose(), TaskScheduler.Default);

        var exitCode = await Task.Run(() => Client().Send(new ViewerMessage { Command = ViewerMessage.Show }), TestContext.Current.CancellationToken);
        await dropped.WaitAsync(TestTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(ExitCode.Failure, exitCode);
    }

    [Fact]
    public void Quit_succeeds_without_sending_anything_when_no_viewer_is_running()
    {
        StartServer();

        var exitCode = Client().Quit(new ViewerMessage { Command = ViewerMessage.Quit });

        Assert.Equal(ExitCode.Ok, exitCode);
        Assert.Empty(_handler.Messages);
    }

    [Fact]
    public void Quit_waits_until_the_viewer_process_is_gone()
    {
        StartServer();
        var checksAfterQuit = 0;
        _launcher.Running = () => _handler.Messages.Count == 0 || ++checksAfterQuit < 5;

        var exitCode = Client().Quit(new ViewerMessage { Command = ViewerMessage.Quit });

        Assert.Equal(ExitCode.Ok, exitCode);
        Assert.Equal(ViewerMessage.Quit, Assert.Single(_handler.Messages).Command);
        Assert.Equal(5, checksAfterQuit);
    }

    [Fact]
    public void Quit_fails_when_the_viewer_does_not_exit()
    {
        StartServer();
        _launcher.Running = () => true;

        var exitCode = Client(Timeouts with { Quit = TimeSpan.FromMilliseconds(200) })
            .Quit(new ViewerMessage { Command = ViewerMessage.Quit });

        Assert.Equal(ExitCode.Failure, exitCode);
        Assert.Contains("AgentImageViewer did not exit in time.", _error.ToString());
    }

    [Fact]
    public void Quit_fails_without_starting_a_viewer_when_the_running_one_cannot_be_reached()
    {
        _launcher.Running = () => true;

        var exitCode = Client(NoViewerYet).Quit(new ViewerMessage { Command = ViewerMessage.Quit });

        Assert.Equal(ExitCode.Failure, exitCode);
        Assert.Equal(0, _launcher.LaunchCount);
    }

    [Fact]
    public async Task A_disposed_server_no_longer_accepts_connections()
    {
        var server = StartServer();
        Assert.Equal(ExitCode.Ok, Client().Send(new ViewerMessage { Command = ViewerMessage.Show }, startViewer: false));

        server.Dispose();
        server.Dispose();

        await WaitUntilAsync(() => Client(NoViewerYet).Send(new ViewerMessage { Command = ViewerMessage.Show }, startViewer: false) == ExitCode.Failure);
    }

    [Fact]
    public void Nothing_is_sent_to_a_server_the_launcher_does_not_recognize()
    {
        StartServer();
        _launcher.ServerRejection = "Not the viewer.";

        var exitCode = Client().Send(new ViewerMessage { Command = ViewerMessage.Show });

        Assert.Equal(ExitCode.Failure, exitCode);
        Assert.Contains("Not the viewer.", _error.ToString());
        Assert.Empty(_handler.Messages);
    }

    private PipeServer StartServer()
    {
        var server = new PipeServer(_pipeName, _handler);
        lock (_servers)
        {
            _servers.Add(server);
        }

        server.Start();
        return server;
    }

    private PipeClient Client(PipeClientTimeouts? timeouts = null) => new(_pipeName, _launcher, _error, timeouts ?? Timeouts);

    private async Task<NamedPipeClientStream> ConnectRawAsync()
    {
        var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(TestTimeout, TestContext.Current.CancellationToken);
        return pipe;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TestTimeout);
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    [Fact]
    public async Task The_server_never_joins_a_pipe_created_by_someone_else_and_takes_the_name_once_it_is_free()
    {
        var squatter = new NamedPipeServerStream(
            _pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        StartServer();
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        // Had the server joined the squatter's pipe, one of these clients would reach the server and be handled.
        // (On Windows the second one finds no free instance; on Linux it waits in the squatter's backlog.
        // A write to the squatter, which never reads, does not complete on Windows; disposing the client ends it.)
        var request = Encoding.UTF8.GetBytes(PipeProtocol.Serialize(new ViewerMessage { Command = ViewerMessage.Show }) + "\n");
        var clients = new List<NamedPipeClientStream>();
        for (var i = 0; i < 2; i++)
        {
            var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            clients.Add(client);
            try
            {
                await client.ConnectAsync(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
                await client.WriteAsync(request, TestContext.Current.CancellationToken).AsTask()
                    .WaitAsync(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
            }
            catch (TimeoutException)
            {
            }
        }

        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        Assert.Empty(_handler.Messages);
        foreach (var client in clients)
        {
            await client.DisposeAsync();
        }

        await squatter.DisposeAsync();

        Assert.Equal(ExitCode.Ok, Client().Send(new ViewerMessage { Command = ViewerMessage.Show }, startViewer: false));
        Assert.Equal(ViewerMessage.Show, Assert.Single(_handler.Messages).Command);
    }

    [Fact]
    public async Task A_message_longer_than_the_limit_is_dropped_without_acknowledgement()
    {
        StartServer();
        using (var pipe = await ConnectRawAsync())
        {
            var chunk = Enumerable.Repeat((byte)'a', 64 * 1024).ToArray();
            try
            {
                for (var written = 0; written <= PipeServer.MaxMessageBytes; written += chunk.Length)
                {
                    await pipe.WriteAsync(chunk, TestContext.Current.CancellationToken);
                }

                await pipe.WriteAsync("\n"u8.ToArray(), TestContext.Current.CancellationToken);
            }
            catch (IOException)
            {
                // The server may hang up as soon as the limit is passed.
            }

            using var reader = new StreamReader(pipe, leaveOpen: true);
            string? response;
            try
            {
                response = await reader.ReadLineAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TestTimeout, TestContext.Current.CancellationToken);
            }
            catch (IOException)
            {
                response = null;
            }

            Assert.Null(response);
        }

        Assert.Empty(_handler.Messages);
        Assert.Equal(ExitCode.Ok, Client().Send(new ViewerMessage { Command = ViewerMessage.Show }));
    }
}
