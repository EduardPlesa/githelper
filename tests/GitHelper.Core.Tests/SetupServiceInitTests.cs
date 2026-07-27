using GitHelper.Core.Content;
using GitHelper.Core.Git;
using GitHelper.Core.Repo;
using GitHelper.Core.Setup;

namespace GitHelper.Core.Tests;

public class SetupServiceInitTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "githelper-setup-" + Guid.NewGuid().ToString("N"));

    public SetupServiceInitTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static SetupService NewService() =>
        new(new GitRunner(), new FolderInspector(), ContentLibrary.Load());

    private static SetupService NewService(IGitRunner runner) =>
        new(runner, new FolderInspector(), ContentLibrary.Load());

    private sealed class StubRunner(GitCommandResult result) : IGitRunner
    {
        public Task<GitCommandResult> RunAsync(
            string workingDirectory, IReadOnlyList<string> args, CancellationToken ct = default)
            => Task.FromResult(result);
    }

    [Fact]
    public async Task PreviewShowsTheCommandAndRunsNothing()
    {
        var preview = await NewService().PreviewAsync(_dir, new SetupRequest("init-repository"));

        Assert.Equal("init -b main", string.Join(' ', preview.CommandLine!.Split(' ').Skip(1)));
        Assert.Null(preview.FileContents);
        Assert.True(preview.CanRun);
        Assert.NotEmpty(preview.Explanation.What);
        Assert.False(Directory.Exists(Path.Combine(_dir, ".git")));
    }

    [Fact]
    public async Task RunCreatesARepositoryOnMain()
    {
        var outcome = await NewService().RunAsync(_dir, new SetupRequest("init-repository"));

        Assert.True(outcome.Success);
        Assert.True(Directory.Exists(Path.Combine(_dir, ".git")));

        var branch = await new GitRunner().RunAsync(_dir, new[] { "branch", "--show-current" });
        Assert.Equal("main", branch.StdOut.Trim());
    }

    [Fact]
    public async Task PreviewIsBlockedWhenTheFolderIsAlreadyARepository()
    {
        await new GitRunner().RunAsync(_dir, new[] { "init", "-q", "-b", "main" });

        var preview = await NewService().PreviewAsync(_dir, new SetupRequest("init-repository"));

        Assert.False(preview.CanRun);
        Assert.Contains(preview.Blockers, b => b.Contains("already", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunRefusesWhenTheFolderBecameARepositoryAfterThePreview()
    {
        var service = NewService();
        var preview = await service.PreviewAsync(_dir, new SetupRequest("init-repository"));
        Assert.True(preview.CanRun);

        // Someone ran `git init` in a terminal meanwhile.
        await new GitRunner().RunAsync(_dir, new[] { "init", "-q", "-b", "main" });

        var outcome = await service.RunAsync(_dir, new SetupRequest("init-repository"));

        Assert.False(outcome.Success);
        Assert.NotEmpty(outcome.Blockers);
    }

    [Fact]
    public async Task AnUnknownOperationIsRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => NewService().PreviewAsync(_dir, new SetupRequest("nonsense")));
    }

    // RunAsync shares the guard with PreviewAsync, but nothing exercised it: a change that
    // dropped the RequireKnown call from RunAsync only would still pass every other test.
    [Fact]
    public async Task RunIsAlsoRejectedForAnUnknownOperation()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => NewService().RunAsync(_dir, new SetupRequest("nonsense")));
    }

    [Fact]
    public async Task RunReportsFailureWithoutNarrationWhenGitInitFails()
    {
        // A stub stands in for a failing `git init` because forcing that to fail for real is
        // not reliable on Windows (e.g. permission denial depends on ACLs we cannot control here).
        var failure = new GitCommandResult(
            new[] { "init", "-b", "main" },
            StdOut: string.Empty,
            StdErr: "fatal: Unable to create '.git/index.lock': Permission denied",
            ExitCode: 128,
            Duration: TimeSpan.FromMilliseconds(5));
        var service = NewService(new StubRunner(failure));

        var outcome = await service.RunAsync(_dir, new SetupRequest("init-repository"));

        Assert.False(outcome.Success);
        // A narration on a failed init would tell a beginner the folder is now tracked when it
        // is not, so this must stay null even though Error is populated.
        Assert.Null(outcome.Narration);
        Assert.NotNull(outcome.Error);
    }
}
