namespace GitHelper.App.Infrastructure;

/// <summary>
/// Marshals work onto the UI thread. Viewmodels depend on this rather than
/// Avalonia's Dispatcher so their tests need no Avalonia application.
/// </summary>
public interface IUiDispatcher
{
    bool IsOnUiThread { get; }

    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread. Callers may post from any thread, but
    /// no two posted actions ever run at once — that is what lets viewmodels touch
    /// ObservableCollections from a Post without locking. Implementations must honour it.
    /// </summary>
    void Post(Action action);
}
