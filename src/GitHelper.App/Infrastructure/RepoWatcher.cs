namespace GitHelper.App.Infrastructure;

/// <summary>
/// Watches a repository for changes and reports them at most once per quiet period.
///
/// A single git command writes many files under .git, so raising a refresh per filesystem
/// event would run dozens of `git status` invocations for one user action. Each event
/// instead restarts a timer, so exactly one callback fires once the churn stops.
/// </summary>
public sealed class RepoWatcher : IDisposable
{
    private readonly TimeSpan _debounce;
    private readonly Action _onChanged;
    private readonly Timer _timer;
    private readonly Lock _gate = new();

    private FileSystemWatcher? _watcher;
    private bool _disposed;

    public RepoWatcher(TimeSpan debounce, Action onChanged)
    {
        _debounce = debounce;
        _onChanged = onChanged;
        _timer = new Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Starts watching a repository root, replacing any previous one.</summary>
    public void Watch(string repoRoot)
    {
        lock (_gate)
        {
            if (_disposed) return;

            _watcher?.Dispose();
            _watcher = null;

            // A recents entry can point at a folder that has since been deleted.
            if (!Directory.Exists(repoRoot)) return;

            // .git is watched too: staging and branch switches show up as writes to
            // .git/index and .git/HEAD, which the UI must notice.
            var watcher = new FileSystemWatcher(repoRoot)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size,
            };

            watcher.Changed += OnFileSystemEvent;
            watcher.Created += OnFileSystemEvent;
            watcher.Deleted += OnFileSystemEvent;
            watcher.Renamed += OnFileSystemEvent;

            // A dropped event is not worth crashing over; the next change re-triggers.
            watcher.Error += (_, _) => { };

            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;

            _disposed = true;
            _watcher?.Dispose();
            _watcher = null;
        }

        // Waited on outside the lock: Fire() acquires the same gate, so blocking here
        // while holding it would deadlock. Timer.Dispose(WaitHandle) signals only once
        // every in-flight callback has finished, so this method cannot return while a
        // callback is still about to invoke _onChanged.
        using var callbacksFinished = new ManualResetEvent(false);
        if (_timer.Dispose(callbacksFinished)) callbacksFinished.WaitOne();
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e) => Restart();

    private void Restart()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void Fire()
    {
        lock (_gate)
        {
            if (_disposed) return;
        }

        // Raised outside the lock so a slow handler cannot block incoming events.
        _onChanged();
    }
}
