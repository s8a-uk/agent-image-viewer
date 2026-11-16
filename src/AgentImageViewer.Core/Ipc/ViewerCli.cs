namespace AgentImageViewer.Core.Ipc;

/// <summary>The command-line client: parses the arguments, sends the command and maps the outcome to an exit code.</summary>
public static class ViewerCli
{
    public static int Run(IReadOnlyList<string> args, PipeClient client, TextWriter error)
    {
        if (!ViewerCommandLine.TryParse(args, out var message, out var parseError))
        {
            error.WriteLine(parseError);
            error.WriteLine(ViewerCommandLine.Usage);
            return ExitCode.Usage;
        }

        return message.Command == ViewerMessage.Quit
            ? client.Quit(message)
            : client.Send(message);
    }
}
