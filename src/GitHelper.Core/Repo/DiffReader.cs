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
    /// <param name="originalPath">
    /// The name this file had before it was renamed, when the snapshot recorded one. Needed
    /// because git scopes rename detection to the paths it is given: without it, a staged
    /// rename comes back as a wholly new file with every line added.
    /// </param>
    public async Task<FileDiff> ReadAsync(
        string repoPath,
        string path,
        DiffSide side,
        string? originalPath = null,
        CancellationToken ct = default)
    {
        var result = await runner.RunAsync(repoPath, ArgsFor(path, side, originalPath), ct);

        // Decided by the shape of the output, never by the exit code. --no-index exits 1
        // whenever the files differ, which is the normal case for every untracked file.
        if (result.StdOut.Length > 0) return DiffParser.Parse(path, result.StdOut);

        // Nothing to show and git was unhappy: a real failure. Reporting it as an empty diff
        // would tell the user their file is unchanged, which is the more dangerous lie.
        if (!result.Success) throw new GitReadException(result);

        return new FileDiff(path, DiffKind.Empty, Array.Empty<DiffHunk>(), Truncated: false);
    }

    private static string[] ArgsFor(string path, DiffSide side, string? originalPath)
    {
        // --no-color because the parser reads text, not ANSI escapes. --no-ext-diff because a
        // configured difftool would return a format this parser has never seen.
        var args = new List<string> { "diff", "--no-color", "--no-ext-diff" };

        switch (side)
        {
            case DiffSide.Unstaged:
                break;

            case DiffSide.Staged:
                args.Add("--cached");
                break;

            case DiffSide.Untracked:
                args.Add("--no-index");
                break;

            default:
                // An enum value that does not exist must fail loudly rather than quietly
                // read a side nobody asked for.
                return new[] { "diff", "--no-color", "--this-flag-does-not-exist" };
        }

        // '--' always, or a file named like a ref makes git guess.
        args.Add("--");

        // /dev/null is git's own spelling for the empty side and is verified to work on
        // Windows; no temporary file is needed.
        if (side == DiffSide.Untracked) args.Add("/dev/null");

        args.Add(path);

        // The old name goes in the pathspec too, because git scopes rename detection to the
        // paths it is given: `diff --cached -- after.txt` reports a new file with every line
        // added, while `diff --cached -- after.txt before.txt` reports the rename git actually
        // recorded. Never for Untracked — --no-index takes exactly two paths, and a file git
        // has never seen has no earlier name.
        if (originalPath is not null && side != DiffSide.Untracked) args.Add(originalPath);

        return args.ToArray();
    }
}
