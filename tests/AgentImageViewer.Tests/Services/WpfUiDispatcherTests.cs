using System.Windows.Threading;
using AgentImageViewer.Services;

namespace AgentImageViewer.Tests.Services;

public sealed class WpfUiDispatcherTests
{
    [Fact]
    public async Task The_action_runs_on_the_dispatcher_thread_and_the_task_completes_after_it()
    {
        var ready = new TaskCompletionSource<Dispatcher>();
        var uiThread = new Thread(() =>
        {
            ready.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        })
        {
            IsBackground = true,
        };
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();
        var dispatcher = await ready.Task;

        try
        {
            Thread? ranOn = null;

            await new WpfUiDispatcher(dispatcher)
                .InvokeAsync(() => ranOn = Thread.CurrentThread)
                .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

            Assert.Same(uiThread, ranOn);
        }
        finally
        {
            dispatcher.InvokeShutdown();
        }
    }
}
