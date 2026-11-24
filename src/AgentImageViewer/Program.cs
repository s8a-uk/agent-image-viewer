using AgentImageViewer.Core.Ipc;
using AgentImageViewer.Services;

namespace AgentImageViewer;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var client = new PipeClient(PipeProtocol.PipeName, new ResidentViewer(), Console.Error);

        // With a command the exe acts as a short-lived client: send to the resident viewer and exit.
        // This path never creates the WPF Application, so hooks stay fast.
        if (args.Length > 0)
        {
            return ViewerCli.Run(args, client, Console.Error);
        }

        using var instance = new Mutex(initiallyOwned: true, PipeProtocol.MutexName, out var isFirst);
        if (!isFirst)
        {
            // Launched again by hand: bring the existing window up instead.
            return client.Send(new ViewerMessage { Command = ViewerMessage.Show }, startViewer: false);
        }

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
