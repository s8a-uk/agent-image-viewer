using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using AgentImageViewer.Core.Ipc;
using Microsoft.Win32.SafeHandles;

namespace AgentImageViewer.Services;

/// <summary>Finds and starts the resident (tray) instance of this executable.</summary>
public sealed class ResidentViewer : IViewerLauncher
{
    // The resident viewer holds this mutex for its whole lifetime.
    public bool IsViewerRunning
    {
        get
        {
            if (!Mutex.TryOpenExisting(PipeProtocol.MutexName, out var mutex))
            {
                return false;
            }

            mutex.Dispose();
            return true;
        }
    }

    /// <summary>
    /// Accepts the server only if it runs this very exe. Another process of the same user (for example a sandboxed,
    /// low-integrity one) could otherwise create the pipe first and receive what the hook sends.
    /// </summary>
    public string? CheckServer(NamedPipeClientStream pipe)
    {
        var server = ServerExecutable(pipe);
        var self = Environment.ProcessPath;
        if (server is not null && self is not null
            && string.Equals(Path.GetFullPath(server), Path.GetFullPath(self), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return server is null
            ? "Could not identify the process that serves the AgentImageViewer pipe."
            : $"The AgentImageViewer pipe is served by {server}, not by this exe. Quit that program and try again.";
    }

    private static string? ServerExecutable(NamedPipeClientStream pipe)
    {
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var processId))
        {
            return null;
        }

        using var process = OpenProcess(ProcessQueryLimitedInformation, inheritHandle: false, processId);
        if (process.IsInvalid)
        {
            return null;
        }

        var buffer = new char[1024];
        var length = buffer.Length;
        return QueryFullProcessImageName(process, 0, buffer, ref length) ? new string(buffer, 0, length) : null;
    }

    public ILaunchedViewer? Launch()
    {
        // UseShellExecute detaches the viewer from this process's handles,
        // so the WSL side does not wait for the resident process to exit.
        var process = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = true,
            WorkingDirectory = AppContext.BaseDirectory,
        });
        return process is null ? null : new LaunchedProcess(process);
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, [Out] char[] exeName, ref int size);

    private sealed class LaunchedProcess(Process process) : ILaunchedViewer
    {
        public bool HasExited => process.HasExited;

        public void Dispose() => process.Dispose();
    }
}
