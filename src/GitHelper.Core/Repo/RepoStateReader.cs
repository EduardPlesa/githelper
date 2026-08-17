using GitHelper.Core.Git;
using GitHelper.Core.Model;
using GitHelper.Core.Parsing;

namespace GitHelper.Core.Repo;

/// <summary>Composes the individual read-only queries into one <see cref="RepoState"/>.</summary>
public sealed class RepoStateReader(IGitRunner runner)
{
    /// <summary>How many commits are loaded for the history view.</summary>
    public const int RecentCommitLimit = 50;

    public async Task<RepoState> ReadAsync(string repoPath, CancellationToken ct = default)
    {
        var statusResult = Demand(await runner.RunAsync(
            repoPath, new[] { "status", "--porcelain=v2", "-z", "--branch" }, ct));
        var status = StatusParser.Parse(statusResult.StdOut);

        var logResult = await runner.RunAsync(
            repoPath,
            new[] { "log", "--format=" + LogParser.Format, "-n", RecentCommitLimit.ToString() },
            ct);
        // A repository with no commits fails this command rather than returning nothing.
        var commits = logResult.Success
            ? LogParser.Parse(logResult.StdOut)
            : Array.Empty<CommitInfo>();

        var branchResult = Demand(await runner.RunAsync(
            repoPath,
            new[] { "for-each-ref", "--format=" + BranchParser.Format, "refs/heads/" },
            ct));
        var branches = BranchParser.Parse(branchResult.StdOut);

        var remoteResult = await runner.RunAsync(repoPath, new[] { "remote" }, ct);
        var hasRemote = remoteResult.Success && remoteResult.StdOut.Trim().Length > 0;

        var tagResult = Demand(await runner.RunAsync(
            repoPath, new[] { "for-each-ref", "--format=" + TagParser.Format, "refs/tags/" }, ct));
        var tags = TagParser.Parse(tagResult.StdOut);

        var stashResult = Demand(await runner.RunAsync(
            repoPath, new[] { "stash", "list", "--format=" + StashParser.Format }, ct));
        var stashes = StashParser.Parse(stashResult.StdOut);

        var operation = await ReadOperationAsync(repoPath, ct);

        return new RepoState(
            RepoRoot: repoPath,
            Branch: status.Branch,
            IsDetached: status.IsDetached,
            Upstream: status.Upstream,
            Ahead: status.Ahead,
            Behind: status.Behind,
            HasCommits: status.HasCommits,
            HasRemote: hasRemote,
            Changes: status.Changes,
            RecentCommits: commits,
            Branches: branches,
            Tags: tags,
            Stashes: stashes,
            Operation: operation);
    }

    /// <summary>
    /// Passes a successful result through, and refuses to carry on with a failed one.
    ///
    /// Only for reads whose output is *described* by the parser rather than *decided* by the
    /// exit code. `git log` failing means "no commits yet", `git remote` failing means "no
    /// remote", and `rev-parse --verify MERGE_HEAD` failing means "no merge in progress" —
    /// those are answers, and they keep their own handling.
    /// </summary>
    private static GitCommandResult Demand(GitCommandResult result)
        => result.Success ? result : throw new GitReadException(result);

    /// <summary>
    /// Whether git has an operation in flight, asked of git rather than answered by looking
    /// for <c>.git/MERGE_HEAD</c> on disk — that path is wrong in a linked worktree, where
    /// <c>.git</c> is a file rather than a directory.
    /// </summary>
    private async Task<OperationState?> ReadOperationAsync(string repoPath, CancellationToken ct)
    {
        // Rebase first: a rebase conflict does not set MERGE_HEAD, so the two cannot collide
        // today, and asking the more specific question first keeps that true if git changes.
        var rebase = await ReadRebaseAsync(repoPath, ct);
        if (rebase is not null) return rebase;

        // Exits non-zero when there is no merge in progress, so the exit code alone answers
        // the question and there is nothing to parse.
        var mergeHead = await runner.RunAsync(
            repoPath, new[] { "rev-parse", "-q", "--verify", "MERGE_HEAD" }, ct);

        if (!mergeHead.Success) return null;

        return new OperationState(
            OperationKind.Merge,
            await NameOfAsync(repoPath, mergeHead.StdOut.Trim(), ct));
    }

    /// <summary>
    /// A paused rebase, or null. git exposes no porcelain for this — status carries no rebase
    /// progress and its long form is human text — so this reads git's own sequencer files.
    /// The path is asked of git rather than assumed, which is what makes it correct in a
    /// linked worktree, where .git is a file rather than a directory.
    /// </summary>
    private async Task<OperationState?> ReadRebaseAsync(string repoPath, CancellationToken ct)
    {
        // rebase-merge is the modern backend; rebase-apply is the older --apply one.
        var directory = await SequencerDirectoryAsync(repoPath, "rebase-merge", ct)
                        ?? await SequencerDirectoryAsync(repoPath, "rebase-apply", ct);

        if (directory is null) return null;

        var onto = ReadSequencerFile(directory, "onto");
        var label = onto is null ? null : await NameOfAsync(repoPath, onto, ct);

        return new OperationState(OperationKind.Rebase, label, await ProgressAsync(repoPath, directory, ct));
    }

    /// <summary>Resolves one sequencer directory through git, or null when it is not there.</summary>
    private async Task<string?> SequencerDirectoryAsync(
        string repoPath, string name, CancellationToken ct)
    {
        var path = await runner.RunAsync(repoPath, new[] { "rev-parse", "--git-path", name }, ct);
        if (!path.Success) return null;

        var relative = path.StdOut.Trim();
        if (relative.Length == 0) return null;

        // --git-path answers relative to the working directory git ran in, which is repoPath.
        // Path.Combine returns the second argument unchanged when it is already absolute.
        var full = Path.Combine(repoPath, relative);
        return Directory.Exists(full) ? full : null;
    }

    /// <summary>
    /// How far through the sequence the rebase is, or null when git does not say. These file
    /// names are not documented API, so a missing or unparseable one costs the counter rather
    /// than the operation.
    /// </summary>
    private async Task<RebaseProgress?> ProgressAsync(
        string repoPath, string directory, CancellationToken ct)
    {
        // rebase-merge names them msgnum/end; rebase-apply names them next/last.
        var stepText = ReadSequencerFile(directory, "msgnum") ?? ReadSequencerFile(directory, "next");
        var totalText = ReadSequencerFile(directory, "end") ?? ReadSequencerFile(directory, "last");

        if (!int.TryParse(stepText, out var step)) return null;
        if (!int.TryParse(totalText, out var total)) return null;

        var stopped = await runner.RunAsync(
            repoPath, new[] { "log", "-1", "--format=%s", "REBASE_HEAD" }, ct);

        var subject = stopped.Success ? stopped.StdOut.Trim() : string.Empty;

        return new RebaseProgress(step, total, subject.Length == 0 ? null : subject);
    }

    /// <summary>One sequencer file's trimmed contents, or null if it is absent or unreadable.</summary>
    private static string? ReadSequencerFile(string directory, string name)
    {
        try
        {
            var path = Path.Combine(directory, name);
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (IOException)
        {
            // Being mid-write is a missing answer, not a fault.
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Turns a commit hash into a name the copy can use, or null when it has none.
    /// This is for display only — nothing is ever run against the result.
    /// </summary>
    private async Task<string?> NameOfAsync(string repoPath, string hash, CancellationToken ct)
    {
        if (hash.Length == 0) return null;

        var named = await runner.RunAsync(
            repoPath, new[] { "name-rev", "--name-only", hash }, ct);

        if (!named.Success) return null;

        var label = named.StdOut.Trim();

        // git prints this literal string when the commit has no reachable name.
        return label is "" or "undefined" ? null : label;
    }

    /// <summary>Returns the repository root containing the given path, or null if there is none.</summary>
    public async Task<string?> FindRepoRootAsync(string path, CancellationToken ct = default)
    {
        var result = await runner.RunAsync(path, new[] { "rev-parse", "--show-toplevel" }, ct);
        if (!result.Success) return null;

        var root = result.StdOut.Trim();
        return root.Length > 0 ? root : null;
    }
}
