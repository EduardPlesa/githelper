using GitHelper.Core.Git;
using GitHelper.Core.Repo;

namespace GitHelper.Core.Tests;

/// <summary>
/// What the reader does when a git command it depends on fails.
///
/// The dangerous answer is "carry on": parsing empty output yields a state with no branch,
/// no commits and no changes, which is indistinguishable from a genuinely empty repository.
/// The UI would show a beginner an empty project and, worse, preconditions would evaluate
/// against fiction — RequiresNoOperationInProgress passes on a false-empty state, so Commit
/// would light up in the middle of a merge and silently finish it.
/// </summary>
public class RepoStateReaderFailureTests
{
    /// <summary>Fails whichever git subcommand it is told to, and succeeds at everything else.</summary>
    private sealed class FailsOneCommand(string failingSubcommand, string stdErr = "boom") : IGitRunner
    {
        public Task<GitCommandResult> RunAsync(
            string workingDirectory, IReadOnlyList<string> args, CancellationToken ct = default)
        {
            var isTarget = args.Count > 0
                && string.Equals(args[0], failingSubcommand, StringComparison.Ordinal);

            // `log` failing is how git reports "no commits yet", and `rev-parse --verify`
            // failing is how it reports "no merge in progress". Both are answers, not faults.
            var exitCode = isTarget ? 128 : args[0] switch
            {
                "log" => 128,
                "rev-parse" => 1,
                _ => 0,
            };

            return Task.FromResult(new GitCommandResult(
                args, string.Empty, exitCode == 0 ? string.Empty : stdErr, exitCode, TimeSpan.Zero));
        }
    }

    [Theory]
    [InlineData("status")]
    [InlineData("for-each-ref")]
    [InlineData("stash")]
    public async Task ReadAsync_RefusesToInventAStateWhenAReadFails(string subcommand)
    {
        var reader = new RepoStateReader(new FailsOneCommand(subcommand));

        await Assert.ThrowsAsync<GitReadException>(() => reader.ReadAsync(@"C:\repos\demo"));
    }

    [Fact]
    public async Task ReadAsync_SaysWhichCommandFailedAndWhatGitSaid()
    {
        var reader = new RepoStateReader(
            new FailsOneCommand("status", "fatal: unable to read index"));

        var error = await Assert.ThrowsAsync<GitReadException>(
            () => reader.ReadAsync(@"C:\repos\demo"));

        Assert.Contains("git status", error.Message, StringComparison.Ordinal);
        Assert.Contains("unable to read index", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_StillTreatsAFailingLogAsARepositoryWithNoCommits()
    {
        // `git log` exits non-zero on a repository with no commits. That is an answer, and
        // the one every brand-new project starts in — it must not become an exception.
        var reader = new RepoStateReader(new FailsOneCommand("nothing-fails"));

        var state = await reader.ReadAsync(@"C:\repos\demo");

        Assert.Empty(state.RecentCommits);
        Assert.Null(state.Operation);
    }
}
