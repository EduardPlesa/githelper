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
    public bool IsStaged => IndexChange != ChangeKind.None;

    public bool HasUnstagedChanges =>
        WorkTreeChange is not (ChangeKind.None or ChangeKind.Untracked);

    public bool IsUntracked => WorkTreeChange == ChangeKind.Untracked;
}
