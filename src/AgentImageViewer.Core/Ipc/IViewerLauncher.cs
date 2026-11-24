using System.IO.Pipes;

namespace AgentImageViewer.Core.Ipc;

/// <summary>Knows whether the resident viewer process is alive, how to start it, and how to recognize it.</summary>
public interface IViewerLauncher
{
    bool IsViewerRunning { get; }

    /// <summary>
    /// Checks that the process serving <paramref name="pipe"/> is the viewer this client belongs to, before anything is
    /// sent. Returns null if it is, or the reason it is not.
    /// </summary>
    string? CheckServer(NamedPipeClientStream pipe);

    /// <summary>Starts the resident viewer. Returns null when the process could not be started.</summary>
    ILaunchedViewer? Launch();
}

/// <summary>A viewer process started by <see cref="IViewerLauncher.Launch"/>.</summary>
public interface ILaunchedViewer : IDisposable
{
    bool HasExited { get; }
}
