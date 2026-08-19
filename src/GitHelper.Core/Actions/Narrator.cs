using GitHelper.Core.Model;

namespace GitHelper.Core.Actions;

/// <summary>
/// Describes what actually changed between two snapshots.
///
/// This deliberately does not know which action ran. Narrating the observed difference
/// rather than the intended one makes it structurally impossible for the app to report a
/// success that did not happen.
/// </summary>
public static class Narrator
{
    public static string Describe(RepoState before, RepoState after)
    {
        var parts = new List<string>();

        DescribeOperation(before, after, parts);

        // While a rebase is in flight on either side, the commit list is not evidence.
        // RecentCommits is HEAD's log, and a rebase detaches HEAD onto the base: the commits
        // still waiting to be replayed are simply absent from it, and diffing would announce
        // them as removed from the history. They have not been removed from anything. A merge
        // never exposed this because a conflicted merge leaves HEAD where it was — so a
        // completed `merge --continue` still gets its "Created commit" sentence here, which is
        // what "The merge is finished." above is deliberately terse and relies on. Scoping this
        // to rebase specifically (rather than "either side has any operation") also means an
        // action like undo-last-commit still gets its "Removed commit ... from the history."
        // when run mid-merge from the History tab — that sentence is true and is the whole
        // point of running it.
        if (before.Operation?.Kind != OperationKind.Rebase && after.Operation?.Kind != OperationKind.Rebase)
            DescribeCommits(before, after, parts);

        DescribeBranch(before, after, parts);
        DescribeStaging(before, after, parts);
        DescribeSync(before, after, parts);

        return parts.Count == 0
            ? "No change that this app can see."
            : string.Join(" ", parts);
    }

    /// <summary>
    /// The only part of narration about something that did not finish. It speaks about
    /// operations starting and ending, and — for a rebase only — about progressing from one
    /// stopped commit to the next, which is the only feedback the user gets that the rebase
    /// is moving. A merge that was already running and still is has nothing new to report;
    /// the file counts speak for themselves in the band.
    /// </summary>
    private static void DescribeOperation(RepoState before, RepoState after, List<string> parts)
    {
        if (before.Operation is null && after.Operation is not null)
        {
            parts.Add(DescribeStop(after));
            return;
        }

        // Both running: for a merge there is nothing new to say, but a rebase moving to the
        // next commit is the whole observable event.
        if (before.Operation is not null && after.Operation is not null)
        {
            var was = before.Operation.Rebase;
            var now = after.Operation.Rebase;

            if (was is not null && now is not null && now.Step > was.Step)
                parts.Add($"Moved on to commit {now.Step} of {now.Total}.");

            return;
        }

        if (before.Operation is null) return;

        // Whether it finished or was called off is observed, not assumed from which action
        // ran — but *how* it is observed differs by kind. A merge never moves HEAD, so a
        // commit having appeared is exactly a bigger RecentCommits count, same as it always
        // was. A rebase detaches HEAD onto the base while it runs, so `before` and `after`
        // are logs of two different tips and comparing their lengths compares nothing: the
        // count goes down when finishing drops a commit (a completing --skip), and it can go
        // up on an abort that simply restores a longer branch. So for a rebase, the outcome
        // asks an identity question instead: is `after`'s tip the exact commit git recorded
        // as the branch's tip before this rebase began (orig-head)? An abort restores that
        // commit precisely — but so can a rebase that paused without ever rewriting anything
        // and then simply ran to completion: a `break`, an `edit` stop the user continues
        // without amending, or a failing `--exec` all pause with the tip still sitting on
        // orig-head. This app's own rebase action can never create that pause, but
        // `rebase-continue` only requires a rebase to already be in flight, and operation
        // state is re-read from disk on every refresh, so a `git rebase -i` started in the
        // user's terminal is fully drivable through the app from there. Identity alone
        // cannot tell that case apart from an abort, so it is not narrated as either — see
        // DescribeRebaseOutcome for the three-way split this produces, and for the fallback
        // used when orig-head could not be read.
        if (before.Operation.Kind == OperationKind.Rebase)
        {
            // Naming the base is the point of the sentence — "up to date" on its own leaves
            // the user to remember what with. Null when the base commit has no reachable
            // name, in which case the sentence stops short rather than inventing one.
            var onto = before.Operation.IncomingLabel;

            parts.Add(DescribeRebaseOutcome(before, after) switch
            {
                RebaseOutcome.Completed => onto is null
                    ? "Your branch is now up to date."
                    : $"Your branch is now up to date with {onto}.",
                RebaseOutcome.Unchanged =>
                    "Nothing changed. Your branch is where it was.",
                _ =>
                    "The update was abandoned. Your branch is back as it was.",
            });
            return;
        }

        var committed = after.RecentCommits.Count > before.RecentCommits.Count;

        parts.Add(committed
            ? "The merge is finished."
            : "The merge was abandoned. Your files are back as they were.");
    }

    /// <summary>The three ways a paused rebase can end, as observed from two snapshots.</summary>
    private enum RebaseOutcome { Completed, Unchanged, Abandoned }

    /// <summary>
    /// Tells apart three outcomes for a paused rebase, decided by identity: is `after`'s tip
    /// still the exact commit git recorded as the branch's tip before the rebase began
    /// (orig-head)?
    ///
    /// A tip that differs from orig-head is Completed — continue, skip, and an auto-drop of
    /// everything already upstream all move the tip somewhere new. A tip identical to
    /// orig-head is Unchanged: an abort restores that commit precisely, but so does a rebase
    /// that paused without ever rewriting anything — a `break`, an `edit` stop continued
    /// without amending, or a failing `--exec` — and was then simply continued to completion.
    /// This app's own rebase action can never create that pause, but `rebase-continue` only
    /// requires a rebase to already be in flight, and operation state is re-read from disk on
    /// every refresh, so a `git rebase -i` started in the user's terminal is fully drivable
    /// through the app from there, making the case reachable. Nothing was written either way
    /// when it happens, so choosing between "finished" and "abandoned" would be guessing —
    /// which is exactly what Unchanged exists to avoid saying.
    ///
    /// When orig-head could not be read — either the `orig-head` file itself is missing, or
    /// <c>RebaseProgress</c> is null wholesale because `msgnum`/`end` (or `next`/`last`)
    /// would not parse, since the whole chain collapses through `?.` — this falls back to
    /// asking whether the commit `before` was sitting on mid-rebase is still reachable in
    /// `after`'s log, and can only report Completed or Abandoned; the fallback has no way to
    /// tell Unchanged apart from either. That fallback also has a known hole of its own: it
    /// is a reachability question, not an identity one, so it answers "completed" for an
    /// abort whenever the rebase base already happens to be an ancestor of the branch — for
    /// instance because the branch had previously merged that base in, which is exactly what
    /// "bring this branch up to date" runs into on a branch that was updated this way before.
    /// Kept only because "orig-head unreadable" is itself already the rare case.
    ///
    /// `before.RecentCommits` cannot be empty against a real repository — a rebase cannot be
    /// running without at least one commit to be running on — but nothing stops a caller from
    /// constructing that state anyway, and guessing at a hash that was never observed would be
    /// exactly the kind of claim this class exists to never make. So an empty `before` falls
    /// back further, to whether anything at all showed up in `after`: nothing sought, nothing
    /// to find, but a commit having appeared from nowhere is still evidence something
    /// completed. An empty `after` answers itself either way — an empty haystack contains
    /// nothing.
    /// </summary>
    private static RebaseOutcome DescribeRebaseOutcome(RepoState before, RepoState after)
    {
        var origHead = before.Operation?.Rebase?.OrigHead;
        if (origHead is not null)
        {
            var afterTip = after.RecentCommits.FirstOrDefault()?.Hash;

            // RepoStateReader swallows a failed `git log` into an empty RecentCommits rather
            // than surfacing the failure, so an empty list here means "the tip is unknown",
            // not "the tip differs from orig-head". Treating null as different would report
            // the rebase Completed on no evidence at all — the one direction this class
            // exists to forbid. Abandoned costs nothing extra to claim (it was already the
            // "not completed" answer) and keeps the guess on the side that never overstates.
            if (afterTip is null) return RebaseOutcome.Abandoned;

            return string.Equals(afterTip, origHead, StringComparison.Ordinal)
                ? RebaseOutcome.Unchanged
                : RebaseOutcome.Completed;
        }

        var sought = before.RecentCommits.Count > 0 ? before.RecentCommits[0].Hash : null;

        var completed = sought is null
            ? after.RecentCommits.Count > 0
            : after.RecentCommits.Any(c => string.Equals(c.Hash, sought, StringComparison.Ordinal));

        return completed ? RebaseOutcome.Completed : RebaseOutcome.Abandoned;
    }

    /// <summary>The sentence for an operation that has just started and immediately stopped.</summary>
    private static string DescribeStop(RepoState after)
    {
        var conflicts = after.Unmerged.Count;

        if (after.Operation!.Kind != OperationKind.Rebase)
        {
            return $"The merge stopped. {conflicts} file(s) have changes git could not "
                   + "combine on its own.";
        }

        var stoppedAt = after.Operation.Rebase?.StoppedAtSubject;
        var where = stoppedAt is null ? "one of your commits" : $"your commit \"{stoppedAt}\"";

        return $"The update stopped on {where}. {conflicts} file(s) have changes git could "
               + "not combine on its own.";
    }

    private static void DescribeCommits(RepoState before, RepoState after, List<string> parts)
    {
        var beforeHashes = before.RecentCommits.Select(c => c.Hash).ToHashSet(StringComparer.Ordinal);
        var afterHashes = after.RecentCommits.Select(c => c.Hash).ToHashSet(StringComparer.Ordinal);

        var added = after.RecentCommits.Where(c => !beforeHashes.Contains(c.Hash)).ToList();
        var removed = before.RecentCommits.Where(c => !afterHashes.Contains(c.Hash)).ToList();

        foreach (var commit in added)
            parts.Add($"Created commit {commit.ShortHash} \"{commit.Subject}\".");

        foreach (var commit in removed)
            parts.Add($"Removed commit {commit.ShortHash} \"{commit.Subject}\" from the history.");
    }

    private static void DescribeBranch(RepoState before, RepoState after, List<string> parts)
    {
        if (string.Equals(before.Branch, after.Branch, StringComparison.Ordinal)) return;

        parts.Add(after.Branch is null
            ? "You are no longer on a branch."
            : $"You are now on branch {after.Branch}.");
    }

    private static void DescribeStaging(RepoState before, RepoState after, List<string> parts)
    {
        var stagedDelta = after.Staged.Count - before.Staged.Count;

        if (stagedDelta > 0)
            parts.Add($"Staged {stagedDelta} file(s).");
        else if (stagedDelta < 0 && after.RecentCommits.Count == before.RecentCommits.Count)
            // A drop in staged files after a commit is already covered by the commit sentence.
            parts.Add($"Unstaged {-stagedDelta} file(s).");
    }

    private static void DescribeSync(RepoState before, RepoState after, List<string> parts)
    {
        if (after.Upstream is null) return;
        if (before.Ahead == after.Ahead && before.Behind == after.Behind) return;

        var position = (after.Ahead, after.Behind) switch
        {
            (0, 0) => $"in step with {after.Upstream}",
            (> 0, 0) => $"{after.Ahead} commit(s) ahead of {after.Upstream}",
            (0, > 0) => $"{after.Behind} commit(s) behind {after.Upstream}",
            var (a, b) => $"{a} ahead of and {b} behind {after.Upstream}",
        };

        parts.Add($"Your branch is now {position}.");
    }
}
