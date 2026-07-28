using GitHelper.Core.Model;

namespace GitHelper.Core.Content;

/// <summary>Fills the {slot} placeholders in authored content from live repository state.</summary>
public static class SlotBinder
{
    /// <summary>How many filenames are listed before the remainder is summarised.</summary>
    private const int FileListLimit = 3;

    /// <summary>
    /// The closed vocabulary. Content referencing a slot outside this set is a content
    /// error caught by the Task 10 tests, never a placeholder left visible at runtime.
    /// </summary>
    public static IReadOnlySet<string> KnownSlots { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "branch", "upstream", "ahead", "behind",
        "stagedCount", "unstagedCount", "untrackedCount",
        "stagedFileList", "path", "branchName", "repoName", "remoteUrl",
    };

    public static IReadOnlyDictionary<string, string> Bind(
        RepoState state,
        string? path = null,
        string? branchName = null,
        string? remoteUrl = null)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["branch"] = state.Branch ?? "no branch (detached)",
            ["upstream"] = state.Upstream ?? "no upstream branch",
            ["ahead"] = state.Ahead.ToString(),
            ["behind"] = state.Behind.ToString(),
            ["stagedCount"] = state.Staged.Count.ToString(),
            ["unstagedCount"] = state.Unstaged.Count.ToString(),
            ["untrackedCount"] = state.Untracked.Count.ToString(),
            ["stagedFileList"] = Summarise(state.Staged.Select(c => c.Path)),
            ["path"] = path ?? "this file",
            ["branchName"] = branchName ?? "the branch",
            ["repoName"] = new DirectoryInfo(state.RepoRoot).Name,
            // Described rather than blank when absent: the panel previews connect-remote
            // before anything has been typed.
            ["remoteUrl"] = string.IsNullOrWhiteSpace(remoteUrl)
                ? "the address you paste"
                : remoteUrl.Trim(),
        };
    }

    /// <summary>Lists a few names, then says how many remain, so a panel never becomes a wall of paths.</summary>
    private static string Summarise(IEnumerable<string> paths)
    {
        var all = paths.ToList();
        if (all.Count == 0) return "no files";

        var shown = string.Join(", ", all.Take(FileListLimit));
        var remaining = all.Count - FileListLimit;
        return remaining > 0 ? $"{shown}, and {remaining} more" : shown;
    }
}
