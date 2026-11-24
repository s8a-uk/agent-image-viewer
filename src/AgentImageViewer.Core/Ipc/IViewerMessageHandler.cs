namespace AgentImageViewer.Core.Ipc;

public interface IViewerMessageHandler
{
    Task HandleAsync(ViewerMessage message);
}
