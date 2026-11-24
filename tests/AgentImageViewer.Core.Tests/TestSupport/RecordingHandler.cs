using System.Collections.Concurrent;
using AgentImageViewer.Core.Ipc;

namespace AgentImageViewer.Core.Tests.TestSupport;

/// <summary>Records the messages the pipe server delivers; optionally holds each one until released.</summary>
public sealed class RecordingHandler : IViewerMessageHandler
{
    private readonly ConcurrentQueue<ViewerMessage> _messages = new();

    public IReadOnlyCollection<ViewerMessage> Messages => _messages;

    /// <summary>When set, handling a message does not finish before this task does.</summary>
    public Task? Gate { get; set; }

    public async Task HandleAsync(ViewerMessage message)
    {
        _messages.Enqueue(message);
        if (Gate is { } gate)
        {
            await gate;
        }
    }
}
