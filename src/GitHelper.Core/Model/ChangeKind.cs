namespace GitHelper.Core.Model;

public enum ChangeKind
{
    None,
    Added,
    Modified,
    Deleted,
    Renamed,
    Copied,
    Untracked,
    Unmerged,
}
