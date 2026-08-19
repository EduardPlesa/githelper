namespace GitHelper.Core.Model;

/// <summary>
/// The kinds of operation git can start and not finish. Merge and rebase exist today;
/// cherry-pick and revert join them as each is built.
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
/// <param name="OrigHead">
/// The branch tip git recorded just before this rebase started, read from the sequencer's
/// <c>orig-head</c> file. An abort restores the branch to exactly this commit — but a
/// completed rebase can land back on it too: this app's own rebase action can never pause
/// without having rewritten something, but an interactive rebase started outside the app can
/// pause with nothing rewritten yet (a `break`, an `edit` stop continued without amending, a
/// failing `--exec`), and finishing from there produces the identical tip. Comparing against
/// this value is still the best identity check available for whether the rebase moved —
/// unlike asking whether some commit is merely still reachable, which a branch that already
/// contains the rebase base (e.g. from an earlier merge) can satisfy by coincidence even
/// after an abort — it just cannot, on its own, distinguish "finished with nothing to do"
/// from "called off"; see <c>Narrator.DescribeRebaseOutcome</c> for how that ambiguity is
/// narrated (as neither, honestly). Null when the file could not be read, riding along with
/// the same nullability as the rest of this record.
/// </param>
public sealed record RebaseProgress(int Step, int Total, string? StoppedAtSubject, string? OrigHead);
