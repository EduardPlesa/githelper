using GitHelper.Core.Content;
using GitHelper.Core.Git;
using GitHelper.Core.Repo;
using GitHelper.Core.Setup;

namespace GitHelper.Core.Tests;

public class SetupServiceGitignoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "githelper-ignore-" + Guid.NewGuid().ToString("N"));

    public SetupServiceGitignoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static SetupService NewService() =>
        new(new GitRunner(), new FolderInspector(), ContentLibrary.Load());

    private string Path_(string name) => System.IO.Path.Combine(_dir, name);

    [Fact]
    public async Task PreviewShowsTheFileRatherThanACommand()
    {
        File.WriteAllText(Path_("App.csproj"), "<Project />");

        var preview = await NewService().PreviewAsync(_dir, new SetupRequest("create-gitignore"));

        Assert.Null(preview.CommandLine);
        Assert.NotNull(preview.FileContents);
        Assert.Contains("bin/", preview.FileContents!);
        Assert.True(preview.CanRun);
        Assert.False(File.Exists(Path_(".gitignore")));
    }

    [Fact]
    public async Task PreviewPicksTheTemplateForTheDetectedProject()
    {
        File.WriteAllText(Path_("package.json"), "{}");

        var preview = await NewService().PreviewAsync(_dir, new SetupRequest("create-gitignore"));

        Assert.Contains("node_modules/", preview.FileContents!);
    }

    [Fact]
    public async Task RunWritesTheFile()
    {
        File.WriteAllText(Path_("main.py"), "print('hi')");

        var outcome = await NewService().RunAsync(_dir, new SetupRequest("create-gitignore"));

        Assert.True(outcome.Success);
        Assert.Contains("__pycache__/", File.ReadAllText(Path_(".gitignore")));
    }

    [Fact]
    public async Task AnExistingGitignoreIsNeverOverwritten()
    {
        File.WriteAllText(Path_(".gitignore"), "my-own-rules\n");

        var preview = await NewService().PreviewAsync(_dir, new SetupRequest("create-gitignore"));

        Assert.False(preview.CanRun);
        Assert.Contains(preview.Blockers, b => b.Contains("already", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunRefusesIfAGitignoreAppearedAfterThePreview()
    {
        var service = NewService();
        var preview = await service.PreviewAsync(_dir, new SetupRequest("create-gitignore"));
        Assert.True(preview.CanRun);

        File.WriteAllText(Path_(".gitignore"), "my-own-rules\n");

        var outcome = await service.RunAsync(_dir, new SetupRequest("create-gitignore"));

        Assert.False(outcome.Success);
        Assert.NotEmpty(outcome.Blockers);
        Assert.Contains(outcome.Blockers, b => b.Contains("already", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("my-own-rules\n", File.ReadAllText(Path_(".gitignore")));
    }

    [Fact]
    public async Task AWriteFailureThatIsNotAnExistingFileGetsAnHonestMessage()
    {
        // The target directory itself does not exist, so the write fails with
        // DirectoryNotFoundException (an IOException) for a reason that has nothing to do
        // with a .gitignore already being there. Reusing the "already exists" wording for
        // this would tell the user something false about their own filesystem.
        var missing = Path_("missing-subfolder");

        var outcome = await NewService().RunAsync(missing, new SetupRequest("create-gitignore"));

        Assert.False(outcome.Success);
        Assert.NotEmpty(outcome.Blockers);
        Assert.DoesNotContain(
            outcome.Blockers, b => b.Contains("already", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(System.IO.Path.Combine(missing, ".gitignore")));
    }
}
