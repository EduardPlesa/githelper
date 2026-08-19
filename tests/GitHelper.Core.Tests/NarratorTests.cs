using GitHelper.Core.Actions;
using GitHelper.Core.Model;

namespace GitHelper.Core.Tests;

public class NarratorTests
{
    private static RepoState State(
        string? branch = "main",
        int ahead = 0,
        int behind = 0,
        CommitInfo[]? commits = null,
        OperationState? operation = null,
        params FileChange[] changes)
        => new(
            @"C:\repos\demo", branch, branch is null, "origin/main", ahead, behind,
            HasCommits: commits is { Length: > 0 },
            HasRemote: true,
            Changes: changes,
            RecentCommits: commits ?? Array.Empty<CommitInfo>(),
            Branches: Array.Empty<BranchInfo>(),
            Tags: Array.Empty<TagInfo>(),
            Stashes: Array.Empty<StashInfo>(),
            Operation: operation);

    private static CommitInfo Commit(string hash, string subject)
        => new(hash + "0000", hash, "Test User", DateTimeOffset.UnixEpoch, subject);

    [Fact]
    public void Describe_ReportsANewCommitWithItsShortHash()
    {
        var before = State(commits: new[] { Commit("aaa", "initial") });
        var after = State(commits: new[] { Commit("bbb", "second"), Commit("aaa", "initial") });

        var narration = Narrator.Describe(before, after);

        Assert.Contains("bbb", narration);
        Assert.Contains("second", narration);
    }

    [Fact]
    public void Describe_ReportsARemovedCommit()
    {
        var before = State(commits: new[] { Commit("bbb", "second"), Commit("aaa", "initial") });
        var after = State(commits: new[] { Commit("aaa", "initial") });

        var narration = Narrator.Describe(before, after);

        Assert.Contains("removed", narration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Describe_ReportsABranchChange()
    {
        var narration = Narrator.Describe(State(branch: "main"), State(branch: "feature"));

        Assert.Contains("feature", narration);
    }

    [Fact]
    public void Describe_ReportsStagingChanges()
    {
        var before = State(changes: new FileChange("a.txt", null, ChangeKind.None, ChangeKind.Modified));
        var after = State(changes: new FileChange("a.txt", null, ChangeKind.Modified, ChangeKind.None));

        var narration = Narrator.Describe(before, after);

        Assert.Contains("staged", narration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Describe_ReportsAheadAndBehindMovement()
    {
        var narration = Narrator.Describe(State(ahead: 2), State(ahead: 0));

        Assert.Contains("origin/main", narration);
    }

    [Fact]
    public void Describe_SaysSoWhenNothingObservablyChanged()
    {
        var narration = Narrator.Describe(State(), State());

        Assert.Contains("no change", narration, StringComparison.OrdinalIgnoreCase);
    }

    private static OperationState Merging(string? from = "feature")
        => new(OperationKind.Merge, from);

    private static FileChange Conflicted(string path)
        => new(path, null, ChangeKind.Unmerged, ChangeKind.Unmerged);

    [Fact]
    public void Describe_ReportsAMergeThatStoppedAndHowManyFilesNeedAttention()
    {
        var after = State(
            operation: Merging(),
            changes: new[] { Conflicted("a.txt"), Conflicted("b.txt") });

        var narration = Narrator.Describe(State(), after);

        Assert.Contains("stopped", narration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2", narration);
    }

    [Fact]
    public void Describe_ReportsAMergeThatFinished()
    {
        var before = State(operation: Merging(), changes: Conflicted("a.txt"));
        var after = State(commits: new[] { Commit("aaa", "Merge branch 'feature'") });

        var narration = Narrator.Describe(before, after);

        Assert.Contains("merge is finished", narration, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// "The merge is finished." is deliberately terse — the design relies on the commit
    /// sentence to carry the hash and subject. A merge is still in flight in `before` (that
    /// is what makes `merge --continue` a merge finishing at all), so a gate that suppressed
    /// the commit list whenever either side had any operation would suppress this sentence
    /// along with it. It must survive narrowing that gate to rebase only.
    /// </summary>
    [Fact]
    public void Describe_ReportsAFinishedMergesCommitAlongsideItsOwnSentence()
    {
        var before = State(operation: Merging(), changes: Conflicted("a.txt"));
        var after = State(commits: new[] { Commit("aaa", "Merge branch 'feature'") });

        var narration = Narrator.Describe(before, after);

        Assert.Contains("Created commit aaa", narration, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_ReportsAMergeThatWasAbandoned()
    {
        // No commit appeared, so the merge ended without producing anything.
        var before = State(operation: Merging(), changes: Conflicted("a.txt"));

        var narration = Narrator.Describe(before, State());

        Assert.Contains("abandoned", narration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Describe_SaysNothingAboutAMergeThatWasAlreadyRunningAndStillIs()
    {
        var before = State(operation: Merging(), changes: new[] { Conflicted("a.txt"), Conflicted("b.txt") });
        var after = State(operation: Merging(), changes: Conflicted("b.txt"));

        var narration = Narrator.Describe(before, after);

        Assert.DoesNotContain("merge", narration, StringComparison.OrdinalIgnoreCase);
    }

    private static OperationState Rebasing(
        int step = 1, int total = 3, string? stoppedAt = "my work", string? origHead = null)
        => new(OperationKind.Rebase, "main", new RebaseProgress(step, total, stoppedAt, origHead));

    [Fact]
    public void Describe_ReportsARebaseThatStoppedAndNamesTheCommit()
    {
        var after = State(operation: Rebasing(), changes: Conflicted("a.txt"));

        var narration = Narrator.Describe(State(), after);

        Assert.Contains("stopped", narration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("my work", narration, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_ReportsMovingOnToTheNextCommitInTheSequence()
    {
        // An operation that was running and still is says nothing for a merge. For a rebase
        // the step advancing is the whole observable event.
        var before = State(operation: Rebasing(step: 1), changes: Conflicted("a.txt"));
        var after = State(operation: Rebasing(step: 2), changes: Conflicted("b.txt"));

        var narration = Narrator.Describe(before, after);

        Assert.Contains("2 of 3", narration, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_ReportsARebaseThatFinished()
    {
        // `before` has a commit sitting where HEAD was mid-rebase, distinct from OrigHead and
        // from anything in `after` — so this only passes if the OrigHead identity check is the
        // thing being exercised, not the "sought is null" fallback (which a before with no
        // commits at all would trigger regardless of whether the real comparison exists).
        var before = State(
            commits: new[] { Commit("bbb", "commit HEAD was on mid-rebase") },
            operation: Rebasing(origHead: "orig0000"),
            changes: Conflicted("a.txt"));
        var after = State(commits: new[] { Commit("aaa", "my work") });

        var narration = Narrator.Describe(before, after);

        // The base is named: "up to date" alone leaves the user to remember what with.
        Assert.Contains("up to date with main", narration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Describe_ReportsARebaseThatFinishedWithoutNamingABaseItCannotName()
    {
        var before = State(
            commits: new[] { Commit("bbb", "commit HEAD was on mid-rebase") },
            operation: new OperationState(
                OperationKind.Rebase, null, new RebaseProgress(1, 1, "my work", "orig0000")),
            changes: Conflicted("a.txt"));
        var after = State(commits: new[] { Commit("aaa", "my work") });

        var narration = Narrator.Describe(before, after);

        Assert.Contains("Your branch is now up to date.", narration, StringComparison.Ordinal);
    }

    /// <summary>
    /// `after`'s tip is exactly OrigHead — the same commit `before` also carries in its own
    /// log — which is what a real abort looks like. But it is also what a rebase that paused
    /// without rewriting anything looks like once it is simply continued to completion: a
    /// `break`, or an `edit` stop finished without amending, leaves the tip sitting on
    /// orig-head too, and Narrator is never told which of the two happened (it is not passed
    /// an action id — only these two snapshots). So it must not guess "abandoned"; the honest
    /// sentence is the same either way. With no commits in `before` at all, this test would
    /// pass even if the real OrigHead comparison were replaced by "anything showed up in
    /// after", which is exactly the vacuous shape this guards against.
    /// </summary>
    [Fact]
    public void Describe_ReportsARebaseAsUnchangedWhenTheTipIsBackAtOrigHead()
    {
        var origHead = Commit("bbb", "commit that was HEAD before the rebase started");
        var before = State(
            commits: new[] { origHead },
            operation: Rebasing(origHead: origHead.Hash),
            changes: Conflicted("a.txt"));

        var narration = Narrator.Describe(before, State(commits: new[] { origHead }));

        Assert.Contains(
            "Nothing changed. Your branch is where it was.",
            narration,
            StringComparison.Ordinal);
        Assert.DoesNotContain("abandoned", narration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("up to date", narration, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// RepoStateReader swallows a failed `git log` into an empty RecentCommits array rather
    /// than surfacing the failure, so an empty `after` here means "the tip is unknown," not
    /// "the tip differs from orig-head." Reporting this as completed would be the one
    /// direction this class exists to forbid: claiming success out of not knowing.
    /// </summary>
    [Fact]
    public void Describe_DoesNotClaimARebaseCompletedWhenAfterHasNoCommitsToCompareAgainst()
    {
        var origHead = Commit("bbb", "commit that was HEAD before the rebase started");
        var before = State(
            commits: new[] { origHead },
            operation: Rebasing(origHead: origHead.Hash),
            changes: Conflicted("a.txt"));

        var narration = Narrator.Describe(before, State());

        Assert.DoesNotContain("up to date", narration, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// When orig-head could not be read — real case: `git am` creates `rebase-apply/` with
    /// `next`/`last` but no `orig-head` file, so `RebaseProgress.OrigHead` is null —
    /// DescribeRebaseOutcome falls back to asking whether the commit `before` was sitting on
    /// mid-rebase is still reachable in `after`'s log. Sought present: completed.
    /// </summary>
    [Fact]
    public void Describe_ReportsARebaseThatFinishedThroughTheFallbackWhenOrigHeadIsUnavailable()
    {
        var midRebase = Commit("ccc", "commit HEAD was on mid-rebase");
        var before = State(
            commits: new[] { midRebase },
            operation: Rebasing(origHead: null),
            changes: Conflicted("a.txt"));
        var after = State(commits: new[] { Commit("aaa", "my work"), midRebase });

        var narration = Narrator.Describe(before, after);

        Assert.Contains("up to date", narration, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The fallback's other outcome: sought absent from `after`'s log — abandoned.</summary>
    [Fact]
    public void Describe_ReportsARebaseThatWasAbandonedThroughTheFallbackWhenOrigHeadIsUnavailable()
    {
        var midRebase = Commit("ccc", "commit HEAD was on mid-rebase");
        var before = State(
            commits: new[] { midRebase },
            operation: Rebasing(origHead: null),
            changes: Conflicted("a.txt"));
        var after = State(commits: new[] { Commit("aaa", "unrelated commit") });

        var narration = Narrator.Describe(before, after);

        Assert.Contains(
            "The update was abandoned. Your branch is back as it was.",
            narration,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_SaysNothingAboutTheCommitListWhileARebaseIsInFlight()
    {
        // RecentCommits is HEAD's log, and a rebase detaches HEAD onto the base: the commits
        // still waiting to be replayed are simply absent from it. Diffing would announce them
        // as removed from the history, which would be a plain lie — nothing was removed.
        var before = State(commits: new[] { Commit("bbb", "my work 2"), Commit("aaa", "my work 1") });
        var after = State(
            commits: new[] { Commit("ccc", "their work") },
            operation: Rebasing(),
            changes: Conflicted("a.txt"));

        var narration = Narrator.Describe(before, after);

        Assert.DoesNotContain("Removed commit", narration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Created commit", narration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stopped", narration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Describe_StillReportsCommitsWhenNoOperationIsInvolvedEitherSide()
    {
        // The suppression above must not reach an ordinary commit or undo, which is where
        // the commit sentences earn their keep.
        var before = State(commits: new[] { Commit("aaa", "initial") });
        var after = State(commits: new[] { Commit("bbb", "second"), Commit("aaa", "initial") });

        Assert.Contains("Created commit", Narrator.Describe(before, after), StringComparison.Ordinal);
    }
}
