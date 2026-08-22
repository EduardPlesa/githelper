using GitHelper.Core.Git;

namespace GitHelper.TestSupport;

/// <summary>A real git repository in a temp directory, deleted on dispose.</summary>
public sealed class TestRepo : IDisposable
{
    private static readonly GitRunner Runner = new();

    public string Path { get; }

    private TestRepo(string path) => Path = path;

    public static async Task<TestRepo> CreateAsync(bool withInitialCommit = true)
    {
        var dir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "githelper-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        var repo = new TestRepo(dir);
        await repo.GitAsync("init", "-q", "-b", "main");
        // Identity and signing are set locally so tests never depend on, or touch,
        // the developer's global git configuration.
        await repo.GitAsync("config", "user.name", "Test User");
        await repo.GitAsync("config", "user.email", "test@example.com");
        await repo.GitAsync("config", "commit.gpgsign", "false");

        if (withInitialCommit)
        {
            repo.WriteFile("README.md", "hello\n");
            await repo.GitAsync("add", "-A");
            await repo.GitAsync("commit", "-q", "-m", "initial");
        }

        return repo;
    }

    public void WriteFile(string relativePath, string content)
    {
        var full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    public Task<GitCommandResult> GitAsync(params string[] args)
        => Runner.RunAsync(Path, args);

    /// <summary>
    /// Leaves the repository part-way through a merge, with one conflicted file: this
    /// branch and <paramref name="branchName"/> have each rewritten the same line.
    /// The merge command itself exits non-zero, which is exactly the case under test.
    /// </summary>
    public async Task StartConflictingMergeAsync(string branchName = "feature")
    {
        WriteFile("conflict.txt", "original\n");
        await GitAsync("add", "-A");
        await GitAsync("commit", "-q", "-m", "add conflict.txt");

        await GitAsync("checkout", "-q", "-b", branchName);
        WriteFile("conflict.txt", "theirs\n");
        await GitAsync("commit", "-q", "-a", "-m", "theirs");

        await GitAsync("checkout", "-q", "main");
        WriteFile("conflict.txt", "ours\n");
        await GitAsync("commit", "-q", "-a", "-m", "ours");

        await GitAsync("merge", "--no-edit", branchName);
    }

    /// <summary>
    /// Leaves the repository part-way through a rebase, stopped on one conflicted commit:
    /// this branch and <paramref name="baseBranch"/> have each rewritten the same line.
    /// The rebase command itself exits non-zero, which is exactly the case under test.
    /// </summary>
    public async Task StartConflictingRebaseAsync(string baseBranch = "main")
    {
        WriteFile("conflict.txt", "original\n");
        await GitAsync("add", "-A");
        await GitAsync("commit", "-q", "-m", "add conflict.txt");

        await GitAsync("checkout", "-q", "-b", "feature");
        WriteFile("conflict.txt", "mine\n");
        await GitAsync("commit", "-q", "-a", "-m", "my work");

        await GitAsync("checkout", "-q", baseBranch);
        WriteFile("conflict.txt", "theirs\n");
        await GitAsync("commit", "-q", "-a", "-m", "their work");

        await GitAsync("checkout", "-q", "feature");
        await GitAsync("rebase", baseBranch);
    }

    /// <summary>
    /// Leaves the repository part-way through a rebase of <b>two</b> conflicting commits,
    /// stopped on the first of them.
    ///
    /// The one-commit fixture above can only ever stop once, so continuing from its single
    /// stop always finishes the rebase. A rebase that stops a second time is a different
    /// code path — git exits non-zero with the sequencer still in place — and it is the one
    /// a real user hits. It needs a second commit whose replay conflicts too, which is what
    /// this builds: "my work 2" rewrites the same line again, so once "my work 1" has been
    /// resolved to anything other than its original text, replaying it conflicts.
    /// </summary>
    /// <returns>
    /// The path of the conflicted file, so a caller can resolve it without repeating the name.
    /// </returns>
    public async Task<string> StartTwiceConflictingRebaseAsync(string baseBranch = "main")
    {
        WriteFile("conflict.txt", "original\n");
        await GitAsync("add", "-A");
        await GitAsync("commit", "-q", "-m", "add conflict.txt");

        await GitAsync("checkout", "-q", "-b", "feature");
        WriteFile("conflict.txt", "mine 1\n");
        await GitAsync("commit", "-q", "-a", "-m", "my work 1");
        WriteFile("conflict.txt", "mine 2\n");
        await GitAsync("commit", "-q", "-a", "-m", "my work 2");

        await GitAsync("checkout", "-q", baseBranch);
        WriteFile("conflict.txt", "theirs\n");
        await GitAsync("commit", "-q", "-a", "-m", "their work");

        await GitAsync("checkout", "-q", "feature");
        await GitAsync("rebase", baseBranch);

        return "conflict.txt";
    }

    /// <summary>
    /// Leaves the repository part-way through a rebase whose base was already merged into
    /// this branch once before — the case a plain reachability check gets wrong. After
    /// `rebase --abort`, <paramref name="baseBranch"/>'s tip is still reachable from the
    /// branch's own log, not because the rebase did anything, but because the earlier merge
    /// already put it there. The rebase itself is real: replaying the branch's pre-merge
    /// divergence against the unmoved base reproduces the exact conflict the earlier merge
    /// resolved by hand, so it stops on the very first commit it tries to replay — leaving
    /// HEAD sitting exactly on the base, unchanged, which is what makes the base's own commit
    /// "reachable in the log" even though nothing from this rebase has been kept.
    /// </summary>
    public async Task StartConflictingRebaseWithPreviouslyMergedBaseAsync(string baseBranch = "main")
    {
        WriteFile("conflict.txt", "line1\nline2\nline3\n");
        await GitAsync("add", "-A");
        await GitAsync("commit", "-q", "-m", "add conflict.txt");

        await GitAsync("checkout", "-q", "-b", "feature");
        WriteFile("conflict.txt", "line1\nmine\nline3\n");
        await GitAsync("commit", "-q", "-a", "-m", "my work");

        await GitAsync("checkout", "-q", baseBranch);
        WriteFile("conflict.txt", "line1\ntheirs\nline3\n");
        await GitAsync("commit", "-q", "-a", "-m", "their work");

        await GitAsync("checkout", "-q", "feature");
        // Conflicts, same as StartConflictingMergeAsync — but here it gets resolved and
        // committed, so the base ends up genuinely merged into this branch's history.
        await GitAsync("merge", "--no-edit", baseBranch);
        WriteFile("conflict.txt", "line1\nmerged\nline3\n");
        await GitAsync("add", "-A");
        await GitAsync("commit", "-q", "-m", $"merge {baseBranch} into feature");

        // One more commit, so the rebase below has something to replay besides the merge
        // (which rebase drops and re-derives from the commits underneath it).
        WriteFile("conflict.txt", "line1\nmerged\nline4\n");
        await GitAsync("commit", "-q", "-a", "-m", "my later work");

        // The base does not move again. Rebasing onto it now replays "my work" — the same
        // edit the earlier merge already reconciled — straight against the base's own
        // content, which is exactly what makes it conflict again.
        await GitAsync("rebase", baseBranch);
    }

    /// <summary>Commits a file, then edits it again without staging: one unstaged change.</summary>
    public async Task<string> AddUnstagedChangeAsync(string relativePath = "tracked.txt")
    {
        WriteFile(relativePath, "one\ntwo\nthree\n");
        await GitAsync("add", "-A");
        await GitAsync("commit", "-q", "-m", "add " + relativePath);

        WriteFile(relativePath, "one\nTWO\nthree\n");
        return relativePath;
    }

    /// <summary>
    /// Stages a pure rename: the contents do not change, only the name. git records it as a
    /// rename in the index, and reports it as one only when both names reach the pathspec —
    /// which is the fact a diff read of a renamed row has to get right.
    /// </summary>
    /// <returns>The old name and the new one.</returns>
    public async Task<(string From, string To)> StageARenameAsync(
        string from = "before.txt", string to = "after.txt")
    {
        WriteFile(from, "one\ntwo\nthree\n");
        await GitAsync("add", "-A");
        await GitAsync("commit", "-q", "-m", "add " + from);

        await GitAsync("mv", from, to);
        return (from, to);
    }

    /// <summary>
    /// Leaves a file staged AND further modified, so the same path has two different diffs.
    /// This is the case the two-rows-one-file UI depends on.
    /// </summary>
    public async Task<string> AddStagedAndFurtherModifiedAsync(string relativePath = "both.txt")
    {
        WriteFile(relativePath, "one\ntwo\n");
        await GitAsync("add", "-A");
        await GitAsync("commit", "-q", "-m", "add " + relativePath);

        WriteFile(relativePath, "one\nSTAGED\n");
        await GitAsync("add", relativePath);
        WriteFile(relativePath, "one\nWORKTREE\n");
        return relativePath;
    }

    public void Dispose()
    {
        try
        {
            // Objects under .git are written read-only; plain recursive delete fails on Windows.
            foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A leaked temp directory must never fail a test run.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
