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
    IReadOnlyList<BranchInfo> Branches,
    IReadOnlyList<TagInfo> Tags,
    IReadOnlyList<StashInfo> Stashes,
    OperationState? Operation)
{
    public IReadOnlyList<FileChange> Staged =>
        Changes.Where(c => c.IsStaged).ToList();

    public IReadOnlyList<FileChange> Unstaged =>
        Changes.Where(c => c.HasUnstagedChanges).ToList();

    public IReadOnlyList<FileChange> Untracked =>
        Changes.Where(c => c.IsUntracked).ToList();

    /// <summary>Files git could not combine on its own, awaiting the user's edits.</summary>
    public IReadOnlyList<FileChange> Unmerged =>
        Changes.Where(c => c.IsUnmerged).ToList();

    public bool HasStagedChanges => Changes.Any(c => c.IsStaged);

    /// <summary>
    /// Conflicts count here even though they count as neither staged nor unstaged: there
    /// plainly are uncommitted changes mid-merge, and the preconditions that refuse to run
    /// against a dirty tree depend on knowing it.
    /// </summary>
    public bool HasUncommittedChanges =>
        Changes.Any(c => c.IsStaged || c.HasUnstagedChanges || c.IsUnmerged);

    /// <summary>
    /// False for the very first commit, which has no parent and therefore cannot be
    /// undone with reset --soft HEAD~1.
    /// </summary>
    public bool CanUndoLastCommit => RecentCommits.Count >= 2;
}
