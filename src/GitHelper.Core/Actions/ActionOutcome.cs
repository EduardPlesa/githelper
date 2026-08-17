using GitHelper.Core.Errors;
using GitHelper.Core.Git;
using GitHelper.Core.Model;

namespace GitHelper.Core.Actions;

/// <summary>The result of running an action, including what observably changed.</summary>
public sealed record ActionOutcome(
    bool Success,
    GitCommandResult Result,
    string? Narration,
    TranslatedError? Error,
    RepoState Before,
    RepoState After,
    IReadOnlyList<PreconditionResult> Blockers)
{
    /// <summary>
    /// True when the command left git part-way through an operation because git stopped and
    /// handed the work back — not because the command failed.
    ///
    /// Derived from observed state rather than from the exit code, because an operation that
    /// stops on conflicts exits non-zero and is not a failure — git did exactly what it was
    /// asked. <see cref="Success"/> keeps meaning the exit code; callers that would
    /// otherwise show an error check this first.
    ///
    /// This only ever describes what a run just did. A merge already in flight when the
    /// repository was opened produces no outcome at all, and the band renders from
    /// <see cref="RepoState.Operation"/> rather than from here.
    /// </summary>
    public bool Paused => IsPaused(Before, After);

    /// <summary>
    /// The one definition of a paused run, shared with <see cref="ActionService"/> so the
    /// error it suppresses and the flag the UI reads can never disagree.
    ///
    /// An operation still in flight afterwards is the signal. It used to be the *transition*
    /// into one, which was true only for merge: a merge stops once, so it always started from
    /// nothing. A rebase stops repeatedly, and `rebase --continue` that stops on the next
    /// commit's conflict exits non-zero with the sequencer still in place — under the old
    /// definition that was reported to the user as an error for a command that did exactly
    /// what was asked.
    ///
    /// Simply "in flight afterwards" is too generous, though: an unrelated command that fails
    /// while a merge or rebase happens to be sitting paused — a bad path handed to
    /// stage-file, say — would have its real error swallowed. So when an operation was
    /// already running, the operation must also have *moved*: git advanced the sequencer, or
    /// HEAD gained the commit that was just replayed. A command that left the operation
    /// exactly where it found it did not pause anything; it failed.
    /// </summary>
    public static bool IsPaused(RepoState before, RepoState after)
    {
        if (after.Operation is null) return false;
        if (before.Operation is null) return true;

        var stepAdvanced =
            before.Operation.Rebase is { } was &&
            after.Operation.Rebase is { } now &&
            now.Step > was.Step;

        return stepAdvanced || HeadMoved(before, after);
    }

    /// <summary>
    /// Whether the tip commit changed. Read as a second, independent signal to the sequencer
    /// counter, whose files are not documented git API and can come back unreadable.
    /// </summary>
    private static bool HeadMoved(RepoState before, RepoState after)
        => !string.Equals(
            before.RecentCommits.FirstOrDefault()?.Hash,
            after.RecentCommits.FirstOrDefault()?.Hash,
            StringComparison.Ordinal);
}
