using Avalonia.Threading;

namespace GitHelper.App.Infrastructure;

public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public bool IsOnUiThread => Dispatcher.UIThread.CheckAccess();

    public void Post(Action action)
    {
        // Run inline when already on the UI thread: queueing unconditionally would delay
        // refreshes that originate on the UI thread by a frame, which reads as flicker.
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
}
