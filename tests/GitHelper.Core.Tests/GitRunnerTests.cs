using GitHelper.Core.Git;

namespace GitHelper.Core.Tests;

public class GitRunnerTests
{
    [Fact]
    public async Task RunAsync_ReportsSuccessAndCapturesStdOut()
    {
        using var repo = await TestRepo.CreateAsync();
        var runner = new GitRunner();

        var result = await runner.RunAsync(repo.Path, new[] { "rev-parse", "--abbrev-ref", "HEAD" });

        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("main", result.StdOut.Trim());
    }

    [Fact]
    public async Task RunAsync_ReportsFailureAndCapturesStdErr()
    {
        using var repo = await TestRepo.CreateAsync();
        var runner = new GitRunner();

        var result = await runner.RunAsync(repo.Path, new[] { "checkout", "no-such-branch" });

        Assert.False(result.Success);
        Assert.NotEqual(0, result.ExitCode);
        Assert.NotEmpty(result.StdErr);
    }

    [Fact]
    public async Task RunAsync_ArgVectorExcludesInternalFlagsSoTheTaughtCommandIsHonest()
    {
        using var repo = await TestRepo.CreateAsync();
        var runner = new GitRunner();

        var result = await runner.RunAsync(repo.Path, new[] { "status" });

        Assert.Equal("git status", result.CommandLine);
    }

    [Fact]
    public async Task RunAsync_HandlesPathsWithSpacesWithoutQuoting()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a file with spaces.txt", "hi\n");

        var runner = new GitRunner();
        var result = await runner.RunAsync(repo.Path, new[] { "add", "--", "a file with spaces.txt" });

        Assert.True(result.Success);
        var staged = await runner.RunAsync(repo.Path, new[] { "diff", "--cached", "--name-only" });
        Assert.Contains("a file with spaces.txt", staged.StdOut);
    }

    [Fact]
    public async Task RunAsync_ProducesLargeOutputWithoutDeadlocking()
    {
        using var repo = await TestRepo.CreateAsync();
        // Far larger than a pipe buffer; a sequential stream read would hang here.
        repo.WriteFile("big.txt", string.Join("\n", Enumerable.Range(0, 200_000).Select(i => $"line {i}")));

        var runner = new GitRunner();
        var task = runner.RunAsync(repo.Path, new[] { "status", "--porcelain", "--untracked-files=all" });
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(30)));

        Assert.Same(task, completed);
        Assert.True((await task).Success);
    }
}
