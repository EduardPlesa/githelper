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
    public void CommandLine_QuotesArgumentsContainingSpacesSoTheDisplayedCommandIsPasteable()
    {
        var result = new GitCommandResult(new[] { "commit", "-m", "add a file" }, "", "", 0, TimeSpan.Zero);

        Assert.Equal("git commit -m \"add a file\"", result.CommandLine);
    }

    [Fact]
    public void CommandLine_LeavesSimpleArgumentsUnquoted()
    {
        var result = new GitCommandResult(new[] { "status" }, "", "", 0, TimeSpan.Zero);

        Assert.Equal("git status", result.CommandLine);
    }

    [Fact]
    public void CommandLine_EscapesEmbeddedDoubleQuotes()
    {
        var result = new GitCommandResult(new[] { "commit", "-m", "say \"hi\" there" }, "", "", 0, TimeSpan.Zero);

        Assert.Equal("git commit -m \"say \\\"hi\\\" there\"", result.CommandLine);
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

    [Fact]
    public async Task RunAsync_NeverLetsGitReachForAnEditor()
    {
        // `merge --continue` and friends launch the configured editor. There is no window
        // for it to appear in, so it would hang forever on a prompt nobody can see — the
        // same reason terminal prompts are already disabled.
        //
        // The ambient environment is deliberately poisoned first: this must hold because
        // the runner sets it, not because whoever launched the app happened to.
        using var repo = await TestRepo.CreateAsync();
        var previous = Environment.GetEnvironmentVariable("GIT_EDITOR");
        Environment.SetEnvironmentVariable("GIT_EDITOR", "not-an-editor");

        try
        {
            var result = await new GitRunner().RunAsync(repo.Path, new[] { "var", "GIT_EDITOR" });

            Assert.Equal("true", result.StdOut.Trim());
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_EDITOR", previous);
        }
    }
}
