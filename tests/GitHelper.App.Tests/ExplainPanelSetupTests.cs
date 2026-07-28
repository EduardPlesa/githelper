using GitHelper.App.ViewModels;
using GitHelper.Core.Actions;
using GitHelper.Core.Content;
using GitHelper.Core.Git;
using GitHelper.Core.Repo;
using GitHelper.Core.Setup;

namespace GitHelper.App.Tests;

public class ExplainPanelSetupTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "githelper-panel-setup-" + Guid.NewGuid().ToString("N"));

    public ExplainPanelSetupTests() => Directory.CreateDirectory(_dir);

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

    private static ExplainPanelViewModel NewPanel()
    {
        var runner = new GitRunner();
        var content = ContentLibrary.Load();
        var actions = new ActionService(runner, new RepoStateReader(runner), content);
        var setup = new SetupService(runner, new FolderInspector(), content);

        return new ExplainPanelViewModel(
            actions, new StubConfirmationDialog(), new InMemorySettingsStore(), setup);
    }

    [Fact]
    public async Task ShowingInitPresentsACommandAndNoFile()
    {
        var panel = NewPanel();

        await panel.ShowSetupAsync(_dir, new SetupRequest("init-repository"));

        Assert.Equal("Start tracking this folder", panel.Title);
        Assert.True(panel.HasCommandLine);
        Assert.False(panel.HasFileContents);
        Assert.NotEmpty(panel.WhatBlocks);
    }

    [Fact]
    public async Task ShowingGitignorePresentsAFileAndNoCommand()
    {
        File.WriteAllText(Path.Combine(_dir, "App.csproj"), "<Project />");
        var panel = NewPanel();

        await panel.ShowSetupAsync(_dir, new SetupRequest("create-gitignore"));

        Assert.True(panel.HasFileContents);
        Assert.False(panel.HasCommandLine);
        Assert.Contains("bin/", panel.FileContents!);
    }

    [Fact]
    public async Task RunningInitCreatesTheRepositoryAndNarrates()
    {
        var panel = NewPanel();
        await panel.ShowSetupAsync(_dir, new SetupRequest("init-repository"));

        var ran = await panel.RunSetupAsync();

        Assert.True(ran);
        Assert.True(Directory.Exists(Path.Combine(_dir, ".git")));
        Assert.False(string.IsNullOrWhiteSpace(panel.Narration));
    }

    [Fact]
    public async Task ABlockedSetupCannotRun()
    {
        await new GitRunner().RunAsync(_dir, new[] { "init", "-q", "-b", "main" });
        var panel = NewPanel();

        await panel.ShowSetupAsync(_dir, new SetupRequest("init-repository"));

        Assert.False(panel.CanRun);
        Assert.True(panel.HasBlockers);
        Assert.False(await panel.RunSetupAsync());
    }

    [Fact]
    public async Task ConfirmingAfterPreviewingAnActionRunsTheActionNotAStaleSetupPreview()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        var panel = NewPanel();

        // Arm the setup path first...
        await panel.ShowSetupAsync(_dir, new SetupRequest("init-repository"));
        // ...then arm the action path. ShowAsync must fully disarm the setup path, or
        // Confirm below would run the stale "init-repository" setup against _dir instead
        // of staging a.txt in the repository the user was just shown.
        await panel.ShowAsync(repo.Path, new ActionRequest("stage-file", Path: "a.txt"));

        await panel.ConfirmCommand.ExecuteAsync(null);

        var state = await new RepoStateReader(new GitRunner()).ReadAsync(repo.Path);
        Assert.Single(state.Staged);
        Assert.False(Directory.Exists(Path.Combine(_dir, ".git")));
        Assert.False(panel.HasFileContents);
    }

    [Fact]
    public async Task ClearResetsTheFileContents()
    {
        File.WriteAllText(Path.Combine(_dir, "App.csproj"), "<Project />");
        var panel = NewPanel();
        await panel.ShowSetupAsync(_dir, new SetupRequest("create-gitignore"));

        panel.Clear();

        Assert.False(panel.HasFileContents);
        Assert.Null(panel.FileContents);
    }
}
