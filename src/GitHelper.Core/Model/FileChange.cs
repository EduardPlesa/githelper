namespace GitHelper.Core.Model;

/// <summary>
/// One changed path. Index and worktree status are kept separate because a file can be
/// both staged and further modified afterwards, and the UI must show that honestly.
/// </summary>
public sealed record FileChange(
    string Path,
    string? OriginalPath,
    ChangeKind IndexChange,
    ChangeKind WorkTreeChange)
{
    /// <summary>
    /// A conflicted file. git reports it with U on both sides, so it would otherwise read
    /// as both staged and modified — it is neither, and both predicates below exclude it.
    /// </summary>
    public bool IsUnmerged => IndexChange == ChangeKind.Unmerged;

    public bool IsStaged => IndexChange is not (ChangeKind.None or ChangeKind.Unmerged);

    public bool HasUnstagedChanges =>
        WorkTreeChange is not (ChangeKind.None or ChangeKind.Untracked or ChangeKind.Unmerged);

    public bool IsUntracked => WorkTreeChange == ChangeKind.Untracked;
}
