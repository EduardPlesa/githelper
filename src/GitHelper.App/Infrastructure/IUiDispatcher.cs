namespace GitHelper.App.Infrastructure;

/// <summary>
/// Marshals work onto the UI thread. Viewmodels depend on this rather than
/// Avalonia's Dispatcher so their tests need no Avalonia application.
/// </summary>
public interface IUiDispatcher
{
    bool IsOnUiThread { get; }

    void Post(Action action);
}
