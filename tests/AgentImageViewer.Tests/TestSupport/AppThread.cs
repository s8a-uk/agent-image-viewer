using System.Windows.Threading;

namespace AgentImageViewer.Tests.TestSupport;

/// <summary>
/// One STA thread that owns the real <see cref="App"/> (with its WPF UI resources), for tests of the app's windows.
/// WPF allows a single Application per process, and its resources may only be used on the thread that created it,
/// so every such test runs its body on this thread.
/// </summary>
public sealed class AppThread : IDisposable
{
    private readonly Dispatcher _dispatcher;

    public AppThread()
    {
        var ready = new TaskCompletionSource<Dispatcher>();
        var thread = new Thread(() =>
        {
            var app = new App();
            app.InitializeComponent();
            ready.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        _dispatcher = ready.Task.WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
    }

    public void Invoke(Action action) => _dispatcher.Invoke(action);

    /// <summary>Processes queued work (layout, Loaded events) on the current dispatcher thread.</summary>
    public static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    public void Dispose() => _dispatcher.InvokeShutdown();
}

/// <summary>Tests that need <see cref="AppThread"/>. They run on their own, after the parallel tests.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AppTests : ICollectionFixture<AppThread>
{
    public const string Name = "App";
}
