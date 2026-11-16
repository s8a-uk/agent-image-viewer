namespace AgentImageViewer.Core.Ipc;

/// <summary>Process exit codes of the command-line client. Hooks depend on these values.</summary>
public static class ExitCode
{
    public const int Ok = 0;
    public const int Failure = 1;
    public const int Usage = 2;
}
