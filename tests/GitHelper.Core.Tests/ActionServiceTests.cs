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

    /// <summary>
    /// Claims the reset succeeded without running it, so the repository is left genuinely
    /// mid-conflict underneath a "success" result — the other half of the honest-fallback
    /// check, independent of RollbackFailingRunner's "the reset itself errors" half.
    /// </summary>
    private sealed class RollbackLyingRunner(IGitRunner inner) : IGitRunner
    {
        public Task<GitCommandResult> RunAsync(
            string workingDirectory, IReadOnlyList<string> args, CancellationToken ct = default)
        {
            if (args.Count == 2 && args[0] == "reset" && args[1] == "--hard")
                return Task.FromResult(new GitCommandResult(args, "", "", 0, TimeSpan.Zero));

            return inner.RunAsync(workingDirectory, args, ct);
        }
    }

    [Fact]
    public async Task RunAsync_ReportsWhenTheRollbackClaimsSuccessButTheTreeIsStillConflicted()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("README.md", "set aside\n");
        await repo.GitAsync("stash", "push", "-q", "-m", "wip");

        repo.WriteFile("README.md", "moved on\n");
        await repo.GitAsync("commit", "-a", "-q", "-m", "moved on");

        var beforePop = await new RepoStateReader(new GitRunner()).ReadAsync(repo.Path);
        var stashRef = beforePop.Stashes.Single().Ref;

        var runner = new RollbackLyingRunner(new GitRunner());
        var service = new ActionService(runner, new RepoStateReader(runner), ContentLibrary.Load());

        var outcome = await service.RunAsync(
            repo.Path, new ActionRequest("stash-pop", StashRef: stashRef));

        Assert.False(outcome.Success);
        Assert.NotNull(outcome.Error);
        Assert.Equal("That clash could not be fully cleared automatically", outcome.Error!.Summary);
    }

    [Fact]
    public async Task RunAsync_DoesNotBlameAnUnrelatedActionForAConflictItDidNotCause()
    {
        // A conflict already sitting in the repo -- from a terminal merge, say -- must not
        // turn some other, unrelated action's success into the stash-rollback failure. The
        // check that catches a stash rollback that did not clear must be scoped to the
        // rollback actually having been attempted.
        using var repo = await TestRepo.CreateAsync();
        await repo.GitAsync("switch", "-c", "other");
        repo.WriteFile("conflict.txt", "two\n");
        await repo.GitAsync("add", "-A");
        await repo.GitAsync("commit", "-q", "-m", "two");

        await repo.GitAsync("switch", "main");
        repo.WriteFile("conflict.txt", "one\n");
        await repo.GitAsync("add", "-A");
        await repo.GitAsync("commit", "-q", "-m", "one");
        await repo.GitAsync("merge", "other");

        repo.WriteFile("unrelated.txt", "x\n");

        var outcome = await NewService().RunAsync(
            repo.Path, new ActionRequest("stage-file", Path: "unrelated.txt"));

        Assert.True(outcome.Success);
        Assert.Null(outcome.Error);
    }

    [Fact]
    public async Task RunAsync_RejectsAnUnknownActionId()
    {
        using var repo = await TestRepo.CreateAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => NewService().RunAsync(repo.Path, new ActionRequest("no-such-action")));
    }

    [Fact]
    public async Task RunAsync_TreatsAMergeThatStoppedAsPausedRatherThanFailed()
    {
        // git merge exits non-zero when it stops on conflicts. That is not a failure — it
        // did exactly what it was asked — so it must not be reported as an error.
        using var repo = await TestRepo.CreateAsync();
        await repo.GitAsync("checkout", "-q", "-b", "feature");
        repo.WriteFile("conflict.txt", "theirs\n");
        await repo.GitAsync("add", "-A");
        await repo.GitAsync("commit", "-q", "-m", "theirs");
        await repo.GitAsync("checkout", "-q", "main");
        repo.WriteFile("conflict.txt", "ours\n");
        await repo.GitAsync("add", "-A");
        await repo.GitAsync("commit", "-q", "-m", "ours");

        var outcome = await NewService().RunAsync(
            repo.Path, new ActionRequest("merge", BranchName: "feature"));

        Assert.True(outcome.Paused);
        Assert.False(outcome.Success);
        Assert.Null(outcome.Error);
        Assert.Contains("stopped", outcome.Narration!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_DoesNotCallAnOrdinaryFailurePaused()
    {
        using var repo = await TestRepo.CreateAsync();

        var outcome = await NewService().RunAsync(
            repo.Path, new ActionRequest("switch-branch", BranchName: "no-such-branch"));

        Assert.False(outcome.Paused);
        Assert.False(outcome.Success);
        Assert.NotNull(outcome.Error);
    }

    [Fact]
    public async Task RunAsync_DoesNotCallASuccessfulActionPaused()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");

        var outcome = await NewService().RunAsync(
            repo.Path, new ActionRequest("stage-file", Path: "a.txt"));

        Assert.True(outcome.Success);
        Assert.False(outcome.Paused);
    }

    /// <summary>
    /// The whole reason `Paused` cannot mean "an operation appeared". A rebase stops once per
    /// conflicting commit; this is the second stop, reached by resolving the first and
    /// continuing. git exits non-zero with the sequencer still in place, and the user who
    /// just did exactly what the banner asked must not be shown an error for it.
    /// </summary>
    [Fact]
    public async Task RunAsync_CallsARebaseThatStopsASecondTimePausedRatherThanFailed()
    {
        using var repo = await TestRepo.CreateAsync();
        var conflicted = await repo.StartTwiceConflictingRebaseAsync();

        // Resolve the first stop by hand and mark it fixed, exactly as the app has the user do.
        repo.WriteFile(conflicted, "reconciled by hand\n");
        await NewService().RunAsync(
            repo.Path, new ActionRequest("mark-resolved", Path: conflicted));

        var outcome = await NewService().RunAsync(repo.Path, new ActionRequest("rebase-continue"));

        // git really did exit non-zero: this is the case the exit code alone gets wrong.
        Assert.False(outcome.Success);

        Assert.True(outcome.Paused);
        Assert.Null(outcome.Error);
        Assert.NotNull(outcome.Narration);

        // Still mid-rebase, and one commit further along than it was.
        Assert.Equal(OperationKind.Rebase, outcome.After.Operation!.Kind);
        Assert.Equal(2, outcome.After.Operation.Rebase!.Step);
        Assert.Contains("2 of 2", outcome.Narration!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other half of the same definition: an unrelated command that fails while an
    /// operation happens to be sitting paused is a failure, not a pause. Without this, "an
    /// operation is in flight afterwards" would swallow the error.
    /// </summary>
    [Fact]
    public async Task RunAsync_StillReportsAnUnrelatedFailureThatHappensMidRebase()
    {
        using var repo = await TestRepo.CreateAsync();
        await repo.StartConflictingRebaseAsync();

        var outcome = await NewService().RunAsync(
            repo.Path, new ActionRequest("stage-file", Path: "no-such-file.txt"));

        Assert.False(outcome.Success);
        Assert.False(outcome.Paused);
        Assert.NotNull(outcome.Error);
    }

    /// <summary>
    /// The stash-conflict copy promises the files were put back and the stash is still there.
    /// Rebase also prints "CONFLICT", and none of that is true mid-rebase, so the rule is
    /// scoped to the two stash actions rather than to the word.
    /// </summary>
    [Fact]
    public async Task RunAsync_DoesNotHandARebaseConflictTheStashConflictCopy()
    {
        using var repo = await TestRepo.CreateAsync();
        await repo.StartTwiceConflictingRebaseAsync();
        repo.WriteFile("conflict.txt", "reconciled by hand\n");
        await NewService().RunAsync(
            repo.Path, new ActionRequest("mark-resolved", Path: "conflict.txt"));

        var outcome = await NewService().RunAsync(repo.Path, new ActionRequest("rebase-continue"));

        // Paused, so there is no error at all — and specifically not that one.
        Assert.Null(outcome.Error);
        Assert.DoesNotContain(
            "stash", outcome.Narration!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Pins the bug a count-based finished/abandoned test had: RecentCommits is HEAD's log,
    /// and a rebase detaches HEAD onto the base while it runs. Aborting restores the branch
    /// (4 commits) over a mid-rebase HEAD sitting on main (3 commits) — a bigger count, which
    /// the old comparison read as "finished" even though the update was just thrown away.
    ///
    /// It must not say "finished" — but it must not say "abandoned" either. Identity against
    /// orig-head is all Narrator has, and a real abort is indistinguishable, from those two
    /// snapshots alone, from a rebase that paused without rewriting anything and was then
    /// simply continued to completion (a `break`, or an `edit` stop finished without
    /// amending). Narrator never sees which one happened, so it says neither — "Nothing
    /// changed" is the honest answer for both.
    /// </summary>
    [Fact]
    public async Task RunAsync_NarratesAnAbortedRebaseAsUnchangedNotFinished()
    {
        using var repo = await TestRepo.CreateAsync();
        await repo.StartTwiceConflictingRebaseAsync();

        var outcome = await NewService().RunAsync(repo.Path, new ActionRequest("rebase-abort"));

        Assert.True(outcome.Success);
        Assert.NotNull(outcome.Narration);
        Assert.Contains("Nothing changed", outcome.Narration!, StringComparison.Ordinal);
        Assert.DoesNotContain("up to date", outcome.Narration!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("abandoned", outcome.Narration!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The mirror bug: a `rebase --skip` that discards the only conflicting commit completes
    /// the rebase by landing back exactly on the base it started mid-rebase on — the same
    /// commit count as before, which the old comparison read as "nothing happened" and
    /// narrated as abandonment of a Destructive action that just destroyed a commit.
    /// </summary>
    [Fact]
    public async Task RunAsync_NarratesASkipThatCompletesTheRebaseAsFinishedNotAbandoned()
    {
        using var repo = await TestRepo.CreateAsync();
        await repo.StartConflictingRebaseAsync();

        var outcome = await NewService().RunAsync(repo.Path, new ActionRequest("rebase-skip"));

        Assert.True(outcome.Success);
        Assert.NotNull(outcome.Narration);
        Assert.Contains("up to date", outcome.Narration!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("abandoned", outcome.Narration!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A third bug in the same family, distinct from the two above: this one survives even a
    /// correct-looking reachability check. When the branch had already merged the rebase base
    /// in earlier (e.g. an earlier "bring this branch up to date"), the base's own commit is
    /// still reachable from the branch's log after `rebase --abort` — not because the rebase
    /// kept anything, but because the earlier merge put it there. A check that asks "is the
    /// base reachable afterwards" answers yes and narrates a completed update; the honest
    /// answer is that the branch's tip is unchanged from before the rebase, same as the
    /// branch's own tip says.
    /// </summary>
    [Fact]
    public async Task RunAsync_NarratesAnAbortedRebaseAsUnchangedEvenWhenTheBaseWasAlreadyMergedIn()
    {
        using var repo = await TestRepo.CreateAsync();
        await repo.StartConflictingRebaseWithPreviouslyMergedBaseAsync();

        var outcome = await NewService().RunAsync(repo.Path, new ActionRequest("rebase-abort"));

        Assert.True(outcome.Success);
        Assert.NotNull(outcome.Narration);
        Assert.Contains("Nothing changed", outcome.Narration!, StringComparison.Ordinal);
        Assert.DoesNotContain("up to date", outcome.Narration!, StringComparison.OrdinalIgnoreCase);
    }
}
