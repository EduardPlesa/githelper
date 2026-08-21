using GitHelper.Core.Git;
using GitHelper.Core.Model;
using GitHelper.Core.Parsing;

namespace GitHelper.Core.Repo;

/// <summary>Which comparison the user is looking at.</summary>
public enum DiffSide
{
    /// <summary>Worktree against index: what is changed but not chosen yet.</summary>
    Unstaged,

    /// <summary>Index against HEAD: what is chosen for the next commit.</summary>
    Staged,

    /// <summary>A file git has never seen, compared against nothing.</summary>
    Untracked,
}

/// <summary>
/// Reads one file's diff, on demand. A sibling of <see cref="RepoStateReader"/> and
/// deliberately not part of it: RepoState is a whole-repo snapshot re-read on every action
/// and every file-watcher tick, and diffs have no bound on their size.
/// </summary>
public sealed class DiffReader(IGitRunner runner)
{
    public async Task<FileDiff> ReadAsync(
        string repoPath,
        string path,
        DiffSide side,
        CancellationToken ct = default)
    {
        var result = await runner.RunAsync(repoPath, ArgsFor(path, side), ct);

        // Decided by the shape of the output, never by the exit code. --no-index exits 1
        // whenever the files differ, which is the normal case for every untracked file.
        if (result.StdOut.Length > 0) return DiffParser.Parse(path, result.StdOut);

        // Nothing to show and git was unhappy: a real failure. Reporting it as an empty diff
        // would tell the user their file is unchanged, which is the more dangerous lie.
        if (!result.Success) throw new GitReadException(result);

        return new FileDiff(path, DiffKind.Empty, Array.Empty<DiffHunk>(), Truncated: false);
    }

    private static string[] ArgsFor(string path, DiffSide side) => side switch
    {
        // --no-color because the parser reads text, not ANSI escapes. --no-ext-diff because a
        // configured difftool would return a format this parser has never seen. And '--'
        // always, or a file named like a ref makes git guess.
        DiffSide.Unstaged =>
            new[] { "diff", "--no-color", "--no-ext-diff", "--", path },
        DiffSide.Staged =>
            new[] { "diff", "--no-color", "--no-ext-diff", "--cached", "--", path },

        // /dev/null is git's own spelling for the empty side and is verified to work on
        // Windows; no temporary file is needed.
        DiffSide.Untracked =>
            new[] { "diff", "--no-color", "--no-ext-diff", "--no-index", "--", "/dev/null", path },

        _ => new[] { "diff", "--no-color", "--this-flag-does-not-exist" },
    };
}
