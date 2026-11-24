using AgentImageViewer.Core.Cards;

namespace AgentImageViewer.Core.Tests.TestSupport;

/// <summary>Runs the work immediately on the calling thread and counts the calls.</summary>
public sealed class InlineUiDispatcher : IUiDispatcher
{
    public int Invocations { get; private set; }

    public Task InvokeAsync(Action action)
    {
        Invocations++;
        action();
        return Task.CompletedTask;
    }
}
