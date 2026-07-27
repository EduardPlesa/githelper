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
}
