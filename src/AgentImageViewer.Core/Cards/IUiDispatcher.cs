namespace AgentImageViewer.Core.Cards;

/// <summary>Runs work on the UI thread.</summary>
public interface IUiDispatcher
{
    /// <summary>Queues <paramref name="action"/> on the UI thread; the task completes when it has run.</summary>
    Task InvokeAsync(Action action);
}
