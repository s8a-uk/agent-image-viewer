namespace AgentImageViewer.Core.Ipc;

/// <summary>How long <see cref="PipeClient"/> waits at each step. The defaults are what the hook relies on.</summary>
public sealed record PipeClientTimeouts
{
    public static PipeClientTimeouts Default { get; } = new();

    /// <summary>First connection attempt; a running viewer answers well within this.</summary>
    public TimeSpan QuickConnect { get; init; } = TimeSpan.FromMilliseconds(300);

    /// <summary>Total time a freshly started viewer gets to open its pipe.</summary>
    public TimeSpan StartupConnect { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan StartupPollInterval { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Time the viewer gets to acknowledge a message.</summary>
    public TimeSpan Response { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Time the viewer gets to exit after acknowledging "quit".</summary>
    public TimeSpan Quit { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan QuitPollInterval { get; init; } = TimeSpan.FromMilliseconds(100);
}
