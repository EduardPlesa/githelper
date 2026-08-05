using GitHelper.Core.Actions;
using GitHelper.Core.Content;
using GitHelper.Core.Git;
using GitHelper.Core.Model;
using GitHelper.Core.Repo;

namespace GitHelper.Core.Tests;

public class ActionServiceTests
{
    private static ActionService NewService()
    {
        var runner = new GitRunner();
        return new ActionService(runner, new RepoStateReader(runner), ContentLibrary.Load());
    }

    [Fact]
    public async Task PreviewAsync_ShowsTheExactCommandWithoutRunningIt()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        await repo.GitAsync("add", "-A");

        var preview = await NewService().PreviewAsync(
            repo.Path, new ActionRequest("commit", Message: "add a file"));

        Assert.Equal("git commit -m \"add a file\"", preview.CommandLine);
        Assert.True(preview.CanRun);

        // Nothing ran: the history is still just the initial commit.
        var log = await repo.GitAsync("log", "--oneline");
        Assert.Single(log.StdOut.Trim().Split('\n'));
    }

    [Fact]
    public async Task PreviewAsync_BindsLiveValuesIntoTheExplanation()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        repo.WriteFile("b.txt", "y\n");
        await repo.GitAsync("add", "-A");

        var preview = await NewService().PreviewAsync(
            repo.Path, new ActionRequest("commit", Message: "two files"));

        Assert.Equal("2", preview.Slots["stagedCount"]);
        Assert.Equal("main", preview.Slots["branch"]);
        Assert.Equal("commit", preview.Explanation.Id);
    }

    [Fact]
    public async Task PreviewAsync_ReportsBlockersWithoutRunningAnything()
    {
        using var repo = await TestRepo.CreateAsync();

        var preview = await NewService().PreviewAsync(
            repo.Path, new ActionRequest("commit", Message: "nothing staged"));

        Assert.False(preview.CanRun);
        Assert.Contains(preview.Blockers, b => b.SuggestedActionId == "stage-all");
    }

    [Fact]
    public async Task PreviewAsync_CarriesTheDangerLevelAndUndoHint()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("README.md", "changed\n");

        var preview = await NewService().PreviewAsync(
            repo.Path, new ActionRequest("discard-file", Path: "README.md"));

        Assert.Equal(Danger.Destructive, preview.Danger);
        Assert.NotEmpty(preview.Explanation.Undo);
    }

    [Fact]
    public async Task RunAsync_ExecutesAndNarratesTheObservedChange()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        await repo.GitAsync("add", "-A");

        var outcome = await NewService().RunAsync(
            repo.Path, new ActionRequest("commit", Message: "add a file"));

        Assert.True(outcome.Success);
        Assert.Contains("add a file", outcome.Narration!);
        Assert.Equal(2, outcome.After.RecentCommits.Count);
        Assert.Single(outcome.Before.RecentCommits);
    }

    [Fact]
    public async Task RunAsync_RevalidatesPreconditionsAndRefusesToRunGit()
    {
        using var repo = await TestRepo.CreateAsync();

        var outcome = await NewService().RunAsync(
            repo.Path, new ActionRequest("commit", Message: "nothing is staged"));

        Assert.False(outcome.Success);
        Assert.NotEmpty(outcome.Blockers);
        Assert.Equal(0, outcome.Result.ExitCode);
        Assert.Empty(outcome.Result.ArgVector); // no command was built or run
        Assert.Single(outcome.After.RecentCommits);
    }

    [Fact]
    public async Task RunAsync_TranslatesAFailureFromGit()
    {
        using var repo = await TestRepo.CreateAsync();

        // No remote is configured, so push fails at the git level rather than at a precondition.
        await repo.GitAsync("remote", "add", "origin", "https://example.invalid/nope.git");
        var outcome = await NewService().RunAsync(repo.Path, new ActionRequest("push"));

        Assert.False(outcome.Success);
        Assert.NotNull(outcome.Error);
        Assert.NotEmpty(outcome.Error!.RawOutput);
    }

    [Fact]
    public async Task RunAsync_BlocksPushInDetachedHeadInsteadOfCrashing()
    {
        using var repo = await TestRepo.CreateAsync();
        await repo.GitAsync("remote", "add", "origin", "https://example.invalid/nope.git");
        var head = await repo.GitAsync("rev-parse", "HEAD");
        await repo.GitAsync("checkout", "-q", head.StdOut.Trim());

        var outcome = await NewService().RunAsync(repo.Path, new ActionRequest("push"));

        Assert.False(outcome.Success);
        Assert.NotEmpty(outcome.Blockers);
        Assert.Contains(outcome.Blockers, b => b.Message!.Contains("detached", StringComparison.OrdinalIgnoreCase));
        // The blocker was caught before git ever ran.
        Assert.Empty(outcome.Result.ArgVector);
    }

    [Fact]
    public async Task PreviewAsync_DoesNotEchoARejectedRemoteUrlBackIntoTheSlots()
    {
        using var repo = await TestRepo.CreateAsync();

        var preview = await NewService().PreviewAsync(
            repo.Path,
            new ActionRequest("connect-remote", RemoteUrl: "https://ghp_exampletoken@github.com/me/project.git"));

        Assert.False(preview.CanRun);
        Assert.DoesNotContain(
            preview.Slots.Values, value => value.Contains("ghp_exampletoken", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_RollsBackAStashPopThatConflictsInsteadOfLeavingAHalfFinishedMerge()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("README.md", "set aside\n");
        await repo.GitAsync("stash", "push", "-q", "-m", "wip");

        // HEAD moves on the same line after the stash was taken, so popping against a clean
        // tree still conflicts — the precondition only rules out unsaved edits, not this.
        repo.WriteFile("README.md", "moved on\n");
        await repo.GitAsync("commit", "-a", "-q", "-m", "moved on");

        var beforePop = await new RepoStateReader(new GitRunner()).ReadAsync(repo.Path);
        var stashRef = beforePop.Stashes.Single().Ref;

        var outcome = await NewService().RunAsync(
            repo.Path, new ActionRequest("stash-pop", StashRef: stashRef));

        Assert.False(outcome.Success);

        var after = await new RepoStateReader(new GitRunner()).ReadAsync(repo.Path);
        Assert.Empty(after.Changes);
        Assert.Equal(
            "moved on\n",
            (await File.ReadAllTextAsync(Path.Combine(repo.Path, "README.md"))).Replace("\r\n", "\n"));

        var stashList = await repo.GitAsync("stash", "list");
        Assert.Contains("wip", stashList.StdOut);

        Assert.NotNull(outcome.Error);
        Assert.Equal("That stash clashes with what's on this branch now", outcome.Error!.Summary);
    }

    /// <summary>
    /// Wraps a real GitRunner but fakes a failure for "reset --hard" without actually running
    /// it — the cheapest way to force the rollback itself to fail while everything else,
    /// including the conflict this is rolling back from, is produced by real git.
    /// </summary>
    private sealed class RollbackFailingRunner(IGitRunner inner) : IGitRunner
    {
        public Task<GitCommandResult> RunAsync(
            string workingDirectory, IReadOnlyList<string> args, CancellationToken ct = default)
        {
            if (args.Count == 2 && args[0] == "reset" && args[1] == "--hard")
                return Task.FromResult(new GitCommandResult(
                    args, "", "fatal: Unable to create '.git/index.lock': File exists.", 128, TimeSpan.Zero));

            return inner.RunAsync(workingDirectory, args, ct);
        }
    }

    [Fact]
    public async Task RunAsync_ReportsWhenTheRollbackAfterAConflictedStashPopItselfFails()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("README.md", "set aside\n");
        await repo.GitAsync("stash", "push", "-q", "-m", "wip");

        // HEAD moves on the same line after the stash was taken, so popping against a clean
        // tree still conflicts — the precondition only rules out unsaved edits, not this.
        repo.WriteFile("README.md", "moved on\n");
        await repo.GitAsync("commit", "-a", "-q", "-m", "moved on");

        var beforePop = await new RepoStateReader(new GitRunner()).ReadAsync(repo.Path);
        var stashRef = beforePop.Stashes.Single().Ref;

        var runner = new RollbackFailingRunner(new GitRunner());
        var service = new ActionService(runner, new RepoStateReader(runner), ContentLibrary.Load());

        var outcome = await service.RunAsync(
            repo.Path, new ActionRequest("stash-pop", StashRef: stashRef));

        Assert.False(outcome.Success);
        Assert.NotNull(outcome.Error);
        Assert.Equal("That clash could not be fully cleared automatically", outcome.Error!.Summary);

        // The reset was faked rather than run, so the tree is genuinely still mid-conflict —
        // this is not an assumption, it is what real git left behind.
        var after = await new RepoStateReader(new GitRunner()).ReadAsync(repo.Path);
        Assert.Contains(
            after.Changes,
            c => c.IndexChange == ChangeKind.Unmerged || c.WorkTreeChange == ChangeKind.Unmerged);
    }

    [Fact]
    public async Task RunAsync_RejectsAnUnknownActionId()
    {
        using var repo = await TestRepo.CreateAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => NewService().RunAsync(repo.Path, new ActionRequest("no-such-action")));
    }
}
