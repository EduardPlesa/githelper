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

    private static OperationState Rebasing(int step = 1, int total = 3, string? stoppedAt = "my work")
        => new(OperationKind.Rebase, "main", new RebaseProgress(step, total, stoppedAt));

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
        var before = State(operation: Rebasing(), changes: Conflicted("a.txt"));
        var after = State(commits: new[] { Commit("aaa", "my work") });

        var narration = Narrator.Describe(before, after);

        // The base is named: "up to date" alone leaves the user to remember what with.
        Assert.Contains("up to date with main", narration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Describe_ReportsARebaseThatFinishedWithoutNamingABaseItCannotName()
    {
        var before = State(
            operation: new OperationState(OperationKind.Rebase, null, new RebaseProgress(1, 1, "my work")),
            changes: Conflicted("a.txt"));
        var after = State(commits: new[] { Commit("aaa", "my work") });

        var narration = Narrator.Describe(before, after);

        Assert.Contains("Your branch is now up to date.", narration, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_ReportsARebaseThatWasAbandoned()
    {
        var before = State(operation: Rebasing(), changes: Conflicted("a.txt"));

        var narration = Narrator.Describe(before, State());

        // The rebase-specific sentence, not merely "abandoned" — which the merge wording
        // also contains, so asserting on it alone discriminates nothing.
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
