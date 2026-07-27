namespace GitHelper.Core.Model;

/// <summary>
/// What can be known about a folder without git. This is the pre-repository domain: before
/// `git init` there is no branch, no commits and no upstream, so RepoState cannot describe it.
/// </summary>
public sealed record FolderState(
    string Path,
    bool IsRepository,
    int FileCount,
    bool HasGitignore,
    ProjectType ProjectType);
