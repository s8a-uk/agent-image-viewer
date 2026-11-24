using System.Windows.Threading;
using AgentImageViewer.Core.Cards;

namespace AgentImageViewer.Services;

public sealed class WpfUiDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    public Task InvokeAsync(Action action) => dispatcher.InvokeAsync(action).Task;
}
