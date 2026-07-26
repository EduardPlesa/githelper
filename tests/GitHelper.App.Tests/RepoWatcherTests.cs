using GitHelper.App.Infrastructure;

namespace GitHelper.App.Tests;

public class RepoWatcherTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "githelper-watch-" + Guid.NewGuid().ToString("N"));

    public RepoWatcherTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    /// <summary>Waits for a condition rather than sleeping a fixed time, to stay fast and stable.</summary>
    private static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(20);
        }
        return condition();
    }

    [Fact]
    public async Task Watch_FiresOnceAfterAFileChanges()
    {
        var fired = 0;
        using var watcher = new RepoWatcher(TimeSpan.FromMilliseconds(80), () => Interlocked.Increment(ref fired));
        watcher.Watch(_dir);

        File.WriteAllText(Path.Combine(_dir, "a.txt"), "x");

        Assert.True(await WaitUntilAsync(() => Volatile.Read(ref fired) >= 1));
    }

    [Fact]
    public async Task Watch_CoalescesABurstOfChangesIntoASingleCallback()
    {
        var fired = 0;
        using var watcher = new RepoWatcher(TimeSpan.FromMilliseconds(100), () => Interlocked.Increment(ref fired));
        watcher.Watch(_dir);

        // Deliberately spread across ~300ms, comfortably longer than the 100ms debounce.
        // A tight loop would finish inside the window and pass even against a throttling
        // implementation, proving nothing: the debounce must RESTART on every event, so a
        // steady stream of changes yields exactly one callback after the stream stops.
        for (var i = 0; i < 12; i++)
        {
            File.WriteAllText(Path.Combine(_dir, $"f{i}.txt"), i.ToString());
            await Task.Delay(25);
        }

        Assert.True(await WaitUntilAsync(() => Volatile.Read(ref fired) >= 1));
        await Task.Delay(300); // let any further callbacks arrive

        Assert.Equal(1, Volatile.Read(ref fired));
    }

    [Fact]
    public async Task Watch_NoticesChangesInsideTheDotGitDirectory()
    {
        // Staging and branch switching show up as .git/index and .git/HEAD writes.
        var gitDir = Path.Combine(_dir, ".git");
        Directory.CreateDirectory(gitDir);
        var fired = 0;
        using var watcher = new RepoWatcher(TimeSpan.FromMilliseconds(80), () => Interlocked.Increment(ref fired));
        watcher.Watch(_dir);

        File.WriteAllText(Path.Combine(gitDir, "index"), "x");

        Assert.True(await WaitUntilAsync(() => Volatile.Read(ref fired) >= 1));
    }

    [Fact]
    public async Task Watch_SwitchingRepositoriesStopsWatchingTheOldOne()
    {
        var second = Path.Combine(_dir, "second");
        Directory.CreateDirectory(second);
        var fired = 0;
        using var watcher = new RepoWatcher(TimeSpan.FromMilliseconds(80), () => Interlocked.Increment(ref fired));

        watcher.Watch(_dir);
        watcher.Watch(second);
        await Task.Delay(200); // let the switch settle
        var baseline = Volatile.Read(ref fired);

        File.WriteAllText(Path.Combine(second, "a.txt"), "x");

        Assert.True(await WaitUntilAsync(() => Volatile.Read(ref fired) > baseline));
    }

    [Fact]
    public async Task Dispose_StopsFiring()
    {
        var fired = 0;
        var watcher = new RepoWatcher(TimeSpan.FromMilliseconds(80), () => Interlocked.Increment(ref fired));
        watcher.Watch(_dir);
        watcher.Dispose();

        File.WriteAllText(Path.Combine(_dir, "a.txt"), "x");
        await Task.Delay(300);

        Assert.Equal(0, Volatile.Read(ref fired));
    }

    [Fact]
    public void Watch_IgnoresAPathThatDoesNotExist()
    {
        using var watcher = new RepoWatcher(TimeSpan.FromMilliseconds(80), () => { });

        // A recents entry can point at a deleted folder; this must not throw.
        watcher.Watch(Path.Combine(_dir, "gone"));
    }

    [Fact]
    public async Task Dispose_WaitsForACallbackThatIsAlreadyRunning()
    {
        using var callbackStarted = new ManualResetEventSlim(false);
        using var releaseCallback = new ManualResetEventSlim(false);
        var callbackFinished = false;

        var watcher = new RepoWatcher(TimeSpan.FromMilliseconds(50), () =>
        {
            callbackStarted.Set();
            releaseCallback.Wait(TimeSpan.FromSeconds(5));
            callbackFinished = true;
        });
        watcher.Watch(_dir);

        File.WriteAllText(Path.Combine(_dir, "a.txt"), "x");
        Assert.True(callbackStarted.Wait(TimeSpan.FromSeconds(5)), "callback never started");

        // Dispose on another thread: it must block until the running callback completes,
        // otherwise a consumer could tear down state the callback is still touching.
        var dispose = Task.Run(() => watcher.Dispose());
        await Task.WhenAny(dispose, Task.Delay(TimeSpan.FromMilliseconds(250)));
        Assert.False(dispose.IsCompleted, "Dispose returned while the callback was still running");

        releaseCallback.Set();
        await Task.WhenAny(dispose, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.True(dispose.IsCompleted, "Dispose never completed");
        Assert.True(callbackFinished);
    }
}
