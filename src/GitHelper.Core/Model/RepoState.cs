namespace GitHelper.Core.Model;

/// <summary>
/// One immutable snapshot of the repository. Every view renders from this, and every
/// precondition is evaluated against it.
/// </summary>
public sealed record RepoState(
    string RepoRoot,
    string? Branch,
    bool IsDetached,
    string? Upstream,
    int Ahead,
    int Behind,
    bool HasCommits,
    bool HasRemote,
    IReadOnlyList<FileChange> Changes,
    IReadOnlyList<CommitInfo> RecentCommits,
    IReadOnlyList<BranchInfo> Branches)
{
    public IReadOnlyList<FileChange> Staged =>
        Changes.Where(c => c.IsStaged).ToList();

    public IReadOnlyList<FileChange> Unstaged =>
        Changes.Where(c => c.HasUnstagedChanges).ToList();

    public IReadOnlyList<FileChange> Untracked =>
        Changes.Where(c => c.IsUntracked).ToList();

    public bool HasStagedChanges => Changes.Any(c => c.IsStaged);

    public bool HasUncommittedChanges =>
        Changes.Any(c => c.IsStaged || c.HasUnstagedChanges);

    /// <summary>
    /// False for the very first commit, which has no parent and therefore cannot be
    /// undone with reset --soft HEAD~1.
    /// </summary>
    public bool CanUndoLastCommit => RecentCommits.Count >= 2;
}
