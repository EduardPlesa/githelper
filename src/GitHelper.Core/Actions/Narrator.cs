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

        // While an operation is in flight on either side, DescribeOperation owns the story
        // and the commit list is not evidence. RecentCommits is HEAD's log, and a rebase
        // detaches HEAD onto the base: the commits still waiting to be replayed are simply
        // absent from it, and diffing would announce them as removed from the history. They
        // have not been removed from anything. A merge never exposed this because a
        // conflicted merge leaves HEAD where it was.
        if (before.Operation is null && after.Operation is null)
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

        // Whether it finished or was called off is the difference between a commit having
        // appeared and not — which is observed, not assumed from which action ran.
        var committed = after.RecentCommits.Count > before.RecentCommits.Count;

        // Naming the base is the point of the sentence — "up to date" on its own leaves the
        // user to remember what with. Null when the base commit has no reachable name, in
        // which case the sentence stops short rather than inventing one.
        var onto = before.Operation.IncomingLabel;

        parts.Add((before.Operation.Kind, committed) switch
        {
            (OperationKind.Rebase, true) => onto is null
                ? "Your branch is now up to date."
                : $"Your branch is now up to date with {onto}.",
            (OperationKind.Rebase, false) =>
                "The update was abandoned. Your branch is back as it was.",
            (_, true) => "The merge is finished.",
            (_, false) => "The merge was abandoned. Your files are back as they were.",
        });
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
