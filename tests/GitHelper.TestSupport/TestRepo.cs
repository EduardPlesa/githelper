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
