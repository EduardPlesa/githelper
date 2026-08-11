using GitHelper.Core.Model;

namespace GitHelper.Core.Tests;

/// <summary>
/// The derived views over <see cref="RepoState.Changes"/>. A conflicted file is reported by
/// git with an index status of U, which naively reads as "staged" — these tests pin the
/// distinction, because Commit must not light up in the middle of a merge.
/// </summary>
public class RepoStateTests
{
    private static FileChange Conflicted(string path)
        => new(path, null, ChangeKind.Unmerged, ChangeKind.Unmerged);

    private static RepoState State(params FileChange[] changes)
        => new(
            RepoRoot: @"C:\repos\demo",
            Branch: "main",
            IsDetached: false,
            Upstream: "origin/main",
            Ahead: 0,
            Behind: 0,
            HasCommits: true,
            HasRemote: true,
            Changes: changes,
            RecentCommits: Array.Empty<CommitInfo>(),
            Branches: Array.Empty<BranchInfo>(),
            Tags: Array.Empty<TagInfo>(),
            Stashes: Array.Empty<StashInfo>(),
            Operation: null);

    [Fact]
    public void AConflictedFileIsNotStaged()
    {
        Assert.False(Conflicted("a.txt").IsStaged);
    }

    [Fact]
    public void AConflictedFileHasNoUnstagedChangesEither()
    {
        // It is neither side of the staged/unstaged split — it is its own thing.
        Assert.False(Conflicted("a.txt").HasUnstagedChanges);
    }

    [Fact]
    public void AConflictedFileIsUnmerged()
    {
        Assert.True(Conflicted("a.txt").IsUnmerged);
    }

    [Fact]
    public void HasStagedChanges_IsFalseWhenTheOnlyChangeIsAConflict()
    {
        Assert.False(State(Conflicted("a.txt")).HasStagedChanges);
    }

    [Fact]
    public void Unmerged_ListsTheConflictedFiles()
    {
        var state = State(
            Conflicted("a.txt"),
            new FileChange("b.txt", null, ChangeKind.Modified, ChangeKind.None));

        Assert.Equal("a.txt", Assert.Single(state.Unmerged).Path);
    }

    [Fact]
    public void HasUncommittedChanges_IsTrueDuringAMerge()
    {
        // There plainly are uncommitted changes mid-merge, and the preconditions that guard
        // against running things on a dirty tree depend on this staying true.
        Assert.True(State(Conflicted("a.txt")).HasUncommittedChanges);
    }
}
