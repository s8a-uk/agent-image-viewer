using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace AgentImageViewer.Core.Ipc;

/// <summary>Sends commands to the resident viewer, starting it first when necessary.</summary>
public sealed class PipeClient
{
    private readonly string _pipeName;
    private readonly IViewerLauncher _launcher;
    private readonly TextWriter _error;
    private readonly PipeClientTimeouts _timeouts;

    /// <param name="error">Receives the reason when a command fails (normally standard error).</param>
    public PipeClient(string pipeName, IViewerLauncher launcher, TextWriter error, PipeClientTimeouts? timeouts = null)
    {
        _pipeName = pipeName;
        _launcher = launcher;
        _error = error;
        _timeouts = timeouts ?? PipeClientTimeouts.Default;
    }

    /// <summary>Sends one message and waits for the acknowledgement. Returns the process exit code.</summary>
    public int Send(ViewerMessage message, bool startViewer = true)
    {
        try
        {
            using var pipe = Connect(startViewer);
            if (pipe is null)
            {
                _error.WriteLine("AgentImageViewer is not running.");
                return ExitCode.Failure;
            }

            // The pipe ACL only proves the server runs as this user. Make sure it is really the viewer before
            // handing over paths and message text.
            if (_launcher.CheckServer(pipe) is { } reason)
            {
                _error.WriteLine(reason);
                return ExitCode.Failure;
            }

            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            using var writer = new StreamWriter(pipe, utf8, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, utf8, leaveOpen: true);

            writer.WriteLine(PipeProtocol.Serialize(message));

            // A hung viewer accepts the connection but never answers; give up so the hook is not blocked.
            var response = reader.ReadLineAsync();
            if (Task.WaitAny([response], _timeouts.Response) < 0)
            {
                _error.WriteLine("AgentImageViewer did not respond in time.");
                return ExitCode.Failure;
            }

            // GetResult rather than Result: a dropped connection surfaces as the IOException handled below.
            return response.GetAwaiter().GetResult() == PipeProtocol.OkResponse ? ExitCode.Ok : ExitCode.Failure;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or Win32Exception)
        {
            _error.WriteLine(ex.Message);
            return ExitCode.Failure;
        }
    }

    /// <summary>
    /// Asks the resident viewer to exit and waits until the process is gone, so the exe can be overwritten.
    /// Succeeds when no viewer is running.
    /// </summary>
    public int Quit(ViewerMessage message)
    {
        if (!_launcher.IsViewerRunning)
        {
            return ExitCode.Ok;
        }

        if (Send(message, startViewer: false) != ExitCode.Ok)
        {
            return ExitCode.Failure;
        }

        var started = Stopwatch.GetTimestamp();
        while (_launcher.IsViewerRunning)
        {
            if (Stopwatch.GetElapsedTime(started) > _timeouts.Quit)
            {
                _error.WriteLine("AgentImageViewer did not exit in time.");
                return ExitCode.Failure;
            }

            Thread.Sleep(_timeouts.QuitPollInterval);
        }

        return ExitCode.Ok;
    }

    private NamedPipeClientStream? Connect(bool startViewer)
    {
        if (TryConnect(_timeouts.QuickConnect) is { } running)
        {
            return running;
        }

        if (!startViewer)
        {
            return null;
        }

        using var viewer = _launcher.Launch();

        // Poll instead of one long Connect so a viewer that crashes on startup
        // fails the hook quickly rather than blocking it for the full timeout.
        var started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) < _timeouts.StartupConnect)
        {
            if (TryConnect(_timeouts.StartupPollInterval) is { } launched)
            {
                return launched;
            }

            // A second instance exits at once when another one already holds the mutex; keep waiting then.
            if (viewer is null || (viewer.HasExited && !_launcher.IsViewerRunning))
            {
                return null;
            }
        }

        return null;
    }

    private NamedPipeClientStream? TryConnect(TimeSpan timeout)
    {
        var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
        try
        {
            pipe.Connect(timeout);
            return pipe;
        }
        catch (TimeoutException)
        {
            pipe.Dispose();
            return null;
        }
        catch
        {
            pipe.Dispose();
            throw;
        }
    }
}
