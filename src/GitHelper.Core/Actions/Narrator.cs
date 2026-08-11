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
        DescribeCommits(before, after, parts);
        DescribeBranch(before, after, parts);
        DescribeStaging(before, after, parts);
        DescribeSync(before, after, parts);

        return parts.Count == 0
            ? "No change that this app can see."
            : string.Join(" ", parts);
    }

    /// <summary>
    /// The only part of narration about something that did not finish. It speaks only about
    /// operations starting and ending — an operation that was already running and still is
    /// has nothing new to report, and the file counts speak for themselves in the band.
    /// </summary>
    private static void DescribeOperation(RepoState before, RepoState after, List<string> parts)
    {
        if (before.Operation is null && after.Operation is not null)
        {
            var conflicts = after.Unmerged.Count;
            parts.Add(
                $"The merge stopped. {conflicts} file(s) have changes git could not "
                + "combine on its own.");
            return;
        }

        if (before.Operation is null || after.Operation is not null) return;

        // Whether it finished or was called off is the difference between a commit having
        // appeared and not — which is observed, not assumed from which action ran.
        var committed = after.RecentCommits.Count > before.RecentCommits.Count;

        parts.Add(committed
            ? "The merge is finished."
            : "The merge was abandoned. Your files are back as they were.");
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
