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
        var statusResult = await runner.RunAsync(
            repoPath, new[] { "status", "--porcelain=v2", "-z", "--branch" }, ct);
        var status = StatusParser.Parse(statusResult.StdOut);

        var logResult = await runner.RunAsync(
            repoPath,
            new[] { "log", "--format=" + LogParser.Format, "-n", RecentCommitLimit.ToString() },
            ct);
        // A repository with no commits fails this command rather than returning nothing.
        var commits = logResult.Success
            ? LogParser.Parse(logResult.StdOut)
            : Array.Empty<CommitInfo>();

        var branchResult = await runner.RunAsync(
            repoPath,
            new[] { "for-each-ref", "--format=" + BranchParser.Format, "refs/heads/" },
            ct);
        var branches = BranchParser.Parse(branchResult.StdOut);

        var remoteResult = await runner.RunAsync(repoPath, new[] { "remote" }, ct);
        var hasRemote = remoteResult.Success && remoteResult.StdOut.Trim().Length > 0;

        var tagResult = await runner.RunAsync(
            repoPath, new[] { "for-each-ref", "--format=" + TagParser.Format, "refs/tags/" }, ct);
        var tags = TagParser.Parse(tagResult.StdOut);

        var stashResult = await runner.RunAsync(
            repoPath, new[] { "stash", "list", "--format=" + StashParser.Format }, ct);
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
    /// Whether git has an operation in flight, asked of git rather than answered by looking
    /// for <c>.git/MERGE_HEAD</c> on disk — that path is wrong in a linked worktree, where
    /// <c>.git</c> is a file rather than a directory.
    /// </summary>
    private async Task<OperationState?> ReadOperationAsync(string repoPath, CancellationToken ct)
    {
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
