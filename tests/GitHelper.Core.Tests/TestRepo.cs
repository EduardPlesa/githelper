using GitHelper.Core.Git;

namespace GitHelper.Core.Tests;

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
