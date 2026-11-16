using System.IO.Pipes;
using AgentImageViewer.Core.Ipc;

namespace AgentImageViewer.Core.Tests.TestSupport;

public sealed class FakeLauncher : IViewerLauncher
{
    public Func<bool> Running { get; set; } = () => false;

    public Func<ILaunchedViewer?> OnLaunch { get; set; } = () => null;

    /// <summary>The reason to reject the server with, or null to accept it.</summary>
    public string? ServerRejection { get; set; }

    public int LaunchCount { get; private set; }

    public bool IsViewerRunning => Running();

    public string? CheckServer(NamedPipeClientStream pipe) => ServerRejection;

    public ILaunchedViewer? Launch()
    {
        LaunchCount++;
        return OnLaunch();
    }
}

public sealed class FakeLaunchedViewer : ILaunchedViewer
{
    public bool HasExited { get; set; }

    public bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;
}
