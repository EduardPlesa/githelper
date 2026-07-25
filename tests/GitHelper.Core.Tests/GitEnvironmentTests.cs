using GitHelper.Core.Git;

namespace GitHelper.Core.Tests;

public class GitEnvironmentTests
{
    /// <summary>A runner returning canned results, so the checks can be tested without a machine state.</summary>
    private sealed class StubRunner(Func<IReadOnlyList<string>, GitCommandResult> respond) : IGitRunner
    {
        public Task<GitCommandResult> RunAsync(
            string workingDirectory, IReadOnlyList<string> args, CancellationToken ct = default)
            => Task.FromResult(respond(args));
    }

    private static GitCommandResult Ok(IReadOnlyList<string> args, string stdOut)
        => new(args, stdOut, "", 0, TimeSpan.Zero);

    private static GitCommandResult Fail(IReadOnlyList<string> args, string stdErr)
        => new(args, "", stdErr, 1, TimeSpan.Zero);

    [Fact]
    public async Task CheckAsync_ReportsEverythingHealthy()
    {
        var runner = new StubRunner(args => args[0] switch
        {
            "--version" => Ok(args, "git version 2.55.0.windows.3"),
            "config" when args[1] == "user.name" => Ok(args, "Ada Lovelace"),
            "config" => Ok(args, "ada@example.com"),
            _ => Fail(args, "unexpected"),
        });

        var checks = await new GitEnvironment(runner).CheckAsync();

        Assert.All(checks, c => Assert.Equal(CheckStatus.Ok, c.Status));
        Assert.True(GitEnvironment.IsUsable(checks));
        Assert.Contains(checks, c => c.Id == "git-version" && c.Summary.Contains("2.55"));
    }

    [Fact]
    public async Task CheckAsync_BlocksWhenGitIsNotInstalled()
    {
        var runner = new StubRunner(_ => throw new System.ComponentModel.Win32Exception("not found"));

        var checks = await new GitEnvironment(runner).CheckAsync();

        var gitCheck = Assert.Single(checks, c => c.Id == "git-present");
        Assert.Equal(CheckStatus.Blocking, gitCheck.Status);
        Assert.False(GitEnvironment.IsUsable(checks));
        Assert.NotNull(gitCheck.FixHint);
    }

    [Fact]
    public async Task CheckAsync_WarnsButDoesNotBlockWhenIdentityIsMissing()
    {
        var runner = new StubRunner(args => args[0] switch
        {
            "--version" => Ok(args, "git version 2.55.0"),
            // git config exits non-zero when the key is unset.
            "config" => Fail(args, ""),
            _ => Fail(args, "unexpected"),
        });

        var checks = await new GitEnvironment(runner).CheckAsync();

        var identity = Assert.Single(checks, c => c.Id == "git-identity");
        Assert.Equal(CheckStatus.Warning, identity.Status);
        Assert.Contains("commit", identity.Explanation, StringComparison.OrdinalIgnoreCase);
        // A missing identity still lets the user browse and stage.
        Assert.True(GitEnvironment.IsUsable(checks));
    }

    [Fact]
    public async Task CheckAsync_WarnsWhenOnlyTheEmailIsMissing()
    {
        var runner = new StubRunner(args => args[0] switch
        {
            "--version" => Ok(args, "git version 2.55.0"),
            "config" when args[1] == "user.name" => Ok(args, "Ada Lovelace"),
            "config" => Fail(args, ""),
            _ => Fail(args, "unexpected"),
        });

        var checks = await new GitEnvironment(runner).CheckAsync();

        Assert.Equal(CheckStatus.Warning, Assert.Single(checks, c => c.Id == "git-identity").Status);
    }

    [Fact]
    public async Task SetIdentityAsync_WritesBothValuesGlobally()
    {
        var calls = new List<IReadOnlyList<string>>();
        var runner = new StubRunner(args =>
        {
            calls.Add(args);
            return Ok(args, "");
        });

        await new GitEnvironment(runner).SetIdentityAsync("Ada Lovelace", "ada@example.com");

        Assert.Equal(2, calls.Count);
        Assert.All(calls, c => Assert.Contains("--global", c));
        Assert.Contains(calls, c => c.Contains("user.name") && c.Contains("Ada Lovelace"));
        Assert.Contains(calls, c => c.Contains("user.email") && c.Contains("ada@example.com"));
    }

    [Fact]
    public async Task CheckAsync_RunsAgainstTheRealGitOnThisMachine()
    {
        var checks = await new GitEnvironment(new GitRunner()).CheckAsync();

        // git is a prerequisite for this project, so it must be present here.
        Assert.Equal(CheckStatus.Ok, Assert.Single(checks, c => c.Id == "git-present").Status);
    }
}
