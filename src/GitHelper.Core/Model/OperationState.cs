namespace GitHelper.Core.Model;

/// <summary>
/// The kinds of operation git can start and not finish. Only merge exists today; rebase,
/// cherry-pick and revert join it as each is built.
/// </summary>
public enum OperationKind
{
    Merge,
    Rebase,
}

/// <summary>
/// An operation git has started and left in flight. Null on <see cref="RepoState"/> means
/// nothing is paused.
///
/// This is read from the repository on every refresh and never cached, so it survives the
/// app being closed and reopened — which it must, because the repository state does.
/// </summary>
/// <param name="IncomingLabel">
/// For a merge, the name of what is being merged in. For a rebase, the base the branch is
/// being replayed onto. Null when the commit has no reachable name, in which case the UI
/// says "another branch" instead.
/// </param>
public sealed record OperationState(
    OperationKind Kind,
    string? IncomingLabel,
    RebaseProgress? Rebase = null);

/// <summary>
/// Where a paused rebase has got to, and which commit it stopped on.
///
/// Null for a merge, which has no steps — and null for a rebase whose progress could not be
/// read, because msgnum and end are not documented git API. A missing counter is reported as
/// missing rather than guessed at.
/// </summary>
/// <param name="Step">1-based position in the sequence.</param>
/// <param name="Total">How many commits are being replayed in total.</param>
/// <param name="StoppedAtSubject">Subject line of the commit git stopped on, or null.</param>
public sealed record RebaseProgress(int Step, int Total, string? StoppedAtSubject);
