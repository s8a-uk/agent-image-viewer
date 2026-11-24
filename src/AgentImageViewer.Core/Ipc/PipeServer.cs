using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace AgentImageViewer.Core.Ipc;

/// <summary>Accepts client connections and hands each message to an <see cref="IViewerMessageHandler"/>.</summary>
/// <remarks>
/// The pipe is only ever joined if this server created it: the first instance is created with
/// <see cref="PipeOptions.FirstPipeInstance"/>, and a replacement instance is always created before the old one is
/// closed, so the name is never released while the server runs. Otherwise another user could create the pipe first
/// with an ACL of their choosing, and the server would add its instances to that pipe and take their commands.
/// </remarks>
public sealed class PipeServer : IDisposable
{
    /// <summary>Longest request line accepted; a message is a few paths and a short text.</summary>
    public const int MaxMessageBytes = 1024 * 1024;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);

    private readonly string _pipeName;
    private readonly IViewerMessageHandler _handler;
    private readonly CancellationTokenSource _cts = new();
    private readonly CancellationToken _stopping;
    private int _disposed;

    public PipeServer(string pipeName, IViewerMessageHandler handler)
    {
        _pipeName = pipeName;
        _handler = handler;
        // Taken once: the token source cannot be asked for it after Dispose.
        _stopping = _cts.Token;
    }

    public void Start() => _ = Task.Run(AcceptLoopAsync);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }

    private async Task AcceptLoopAsync()
    {
        NamedPipeServerStream? listening = null;
        try
        {
            while (!_stopping.IsCancellationRequested)
            {
                try
                {
                    listening ??= CreatePipe(first: true);
                    await listening.WaitForConnectionAsync(_stopping);

                    // Open the next instance before handling this connection, so there is never a moment
                    // without a listening pipe: a client that reconnects right after its acknowledgement
                    // must not find the pipe missing.
                    var connected = listening;
                    listening = null;
                    try
                    {
                        listening = CreatePipe(first: false);
                    }
                    finally
                    {
                        _ = HandleConnectionAsync(connected);
                    }
                }
                catch (IOException) when (listening is not null)
                {
                    // The instance broke (e.g. a client vanished while connecting). Replace it before closing it,
                    // so the pipe keeps existing and nobody else can create it in between.
                    var broken = listening;
                    listening = null;
                    try
                    {
                        listening = CreatePipe(first: false);
                    }
                    catch (IOException)
                    {
                        await Task.Delay(RetryDelay, _stopping);
                    }
                    finally
                    {
                        await broken.DisposeAsync();
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // No instance of ours exists: either the previous ones are still closing, or someone else owns the
                    // name. Keep trying; the server must never join a pipe it did not create.
                    await Task.Delay(RetryDelay, _stopping);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Disposed: stop accepting.
        }
        finally
        {
            if (listening is not null)
            {
                await listening.DisposeAsync();
            }
        }
    }

    // Only call with first: false while another instance of this server is open, so the pipe is known to be ours.
    private NamedPipeServerStream CreatePipe(bool first) => new(
        _pipeName,
        PipeDirection.InOut,
        NamedPipeServerStream.MaxAllowedServerInstances,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None));

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe)
    {
        await using (pipe)
        {
            try
            {
                var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                await using var writer = new StreamWriter(pipe, utf8, leaveOpen: true) { AutoFlush = true };

                // A client that never finishes its line must not hold the connection or grow memory forever.
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stopping);
                timeout.CancelAfter(ReadTimeout);
                var line = await ReadLineAsync(pipe, utf8, timeout.Token);
                var message = line is null ? null : PipeProtocol.Deserialize(line);

                if (message is not null)
                {
                    // Reply only after handling, so a client's next command
                    // (e.g. two posts in a row) is always processed in order.
                    await _handler.HandleAsync(message);
                }

                await writer.WriteLineAsync(PipeProtocol.OkResponse);
            }
            catch (Exception ex) when (ex is IOException or JsonException or OperationCanceledException or InvalidDataException)
            {
                // The client is gone or sent garbage; there is nobody to report to.
            }
        }
    }

    /// <summary>Reads up to the first newline. Returns null at end of stream; throws past <see cref="MaxMessageBytes"/>.</summary>
    private static async Task<string?> ReadLineAsync(Stream stream, Encoding encoding, CancellationToken cancellationToken)
    {
        using var line = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return line.Length == 0 ? null : Decode(line);
            }

            var newline = Array.IndexOf(buffer, (byte)'\n', 0, read);
            var count = newline >= 0 ? newline : read;
            if (line.Length + count > MaxMessageBytes)
            {
                throw new InvalidDataException("The message is too long.");
            }

            line.Write(buffer, 0, count);
            if (newline >= 0)
            {
                return Decode(line);
            }
        }

        string Decode(MemoryStream bytes) => encoding.GetString(bytes.GetBuffer(), 0, (int)bytes.Length).TrimEnd('\r');
    }
}
