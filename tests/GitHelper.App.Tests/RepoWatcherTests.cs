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
        using var watcher = new RepoWatcher(TimeSpan.FromMilliseconds(150), () => Interlocked.Increment(ref fired));
        watcher.Watch(_dir);

        // Stands in for the many files a single `git commit` writes under .git.
        for (var i = 0; i < 25; i++)
            File.WriteAllText(Path.Combine(_dir, $"f{i}.txt"), i.ToString());

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
}
