namespace GitHelper.Core.Model;

/// <summary>
/// One file's changes, as git printed them. Read on demand and deliberately not part of
/// <see cref="RepoState"/>: that snapshot is re-read on every watcher tick and must stay
/// cheap, and a diff has no bound on its size.
/// </summary>
public sealed record FileDiff(
    string Path,
    DiffKind Kind,
    IReadOnlyList<DiffHunk> Hunks,
    /// <summary>
    /// Only ever meaningful for <see cref="DiffKind.Text"/>. A separate flag rather than a
    /// fourth DiffKind, because truncation is not an alternative to being binary.
    /// </summary>
    bool Truncated);

public enum DiffKind
{
    Text,
    Binary,

    /// <summary>No differences. Not an error: a file can be edited and reverted between
    /// the snapshot being taken and the diff being read.</summary>
    Empty,
}

/// <summary>
/// One run of changed lines and the context around it. <paramref name="Header"/> is kept
/// verbatim as well as parsed, because the UI shows it and underlines it as jargon rather
/// than hiding it.
/// </summary>
public sealed record DiffHunk(
    string Header,
    int OldStart,
    int OldCount,
    int NewStart,
    int NewCount,
    IReadOnlyList<DiffLine> Lines);

/// <summary>
/// One line. The numbers are null rather than zero on the side where the line does not
/// exist — the same reason RebaseProgress is null for a merge instead of carrying 0 of 0.
/// </summary>
public sealed record DiffLine(
    DiffLineKind Kind,
    string Text,
    int? OldLineNumber,
    int? NewLineNumber);

public enum DiffLineKind
{
    Context,
    Added,
    Removed,

    /// <summary>Git's own "\ No newline at end of file". Printed, not context, and not hidden:
    /// hiding it would make this app's diff disagree with the terminal's.</summary>
    NoNewlineMarker,
}
