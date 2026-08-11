using GitHelper.Core.Git;

namespace GitHelper.Core.Tests;

/// <summary>
/// git does not support two index-writing commands in one repository at once — the second
/// dies with "Unable to create index.lock". The app hits this for real: staging writes
/// .git/index, the file watcher wakes and runs `git status`, which takes index.lock to write
/// back its refreshed stat cache, and a commit started in that window fails outright.
/// </summary>
public class SerializedGitRunnerTests
{
    /// <summary>Records the highest number of calls observed inside the inner runner at once.</summary>
    private sealed class OverlapProbe : IGitRunner
    {
        private int _inFlight;

        public int PeakConcurrent { get; private set; }

        public async Task<GitCommandResult> RunAsync(
            string workingDirectory, IReadOnlyList<string> args, CancellationToken ct = default)
        {
            var now = Interlocked.Increment(ref _inFlight);
            if (now > PeakConcurrent) PeakConcurrent = now;

            // Long enough that unserialized callers would reliably overlap.
            await Task.Delay(20, ct);

            Interlocked.Decrement(ref _inFlight);
            return new GitCommandResult(args, string.Empty, string.Empty, 0, TimeSpan.Zero);
        }
    }

    [Fact]
    public async Task RunAsync_NeverRunsTwoCommandsAgainstOneRepositoryAtOnce()
    {
        var probe = new OverlapProbe();
        var runner = new SerializedGitRunner(probe);

        await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => runner.RunAsync(@"C:\repos\demo", new[] { "status" })));

        Assert.Equal(1, probe.PeakConcurrent);
    }

    [Fact]
    public async Task RunAsync_LetsDifferentRepositoriesRunAtTheSameTime()
    {
        // The lock is per repository, not global: two projects open at once must not queue
        // behind each other, and git has no problem with it.
        var probe = new OverlapProbe();
        var runner = new SerializedGitRunner(probe);

        await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(i => runner.RunAsync($@"C:\repos\demo{i}", new[] { "status" })));

        Assert.True(probe.PeakConcurrent > 1, "different repositories should not serialize");
    }

    [Fact]
    public async Task RunAsync_TreatsTheSameRepositoryReachedByDifferentCasingAsOne()
    {
        // Windows paths are case-insensitive, and the same repository arrives spelled both
        // ways: the watcher reports what the OS gives it, rev-parse reports git's own.
        var probe = new OverlapProbe();
        var runner = new SerializedGitRunner(probe);

        await Task.WhenAll(
            runner.RunAsync(@"C:\repos\Demo", new[] { "status" }),
            runner.RunAsync(@"c:\repos\demo", new[] { "commit" }),
            runner.RunAsync(@"C:\REPOS\DEMO", new[] { "status" }));

        Assert.Equal(1, probe.PeakConcurrent);
    }

    [Fact]
    public async Task RunAsync_ReturnsTheInnerResultUnchanged()
    {
        var runner = new SerializedGitRunner(new OverlapProbe());

        var result = await runner.RunAsync(@"C:\repos\demo", new[] { "status", "--short" });

        Assert.True(result.Success);
        Assert.Equal(new[] { "status", "--short" }, result.ArgVector);
    }
}
