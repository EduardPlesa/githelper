using GitHelper.App.ViewModels;
using GitHelper.Core.Actions;
using GitHelper.Core.Content;
using GitHelper.Core.Git;
using GitHelper.Core.Model;
using GitHelper.Core.Repo;
using GitHelper.Core.Setup;

namespace GitHelper.App.Tests;

public class ChangesGitignoreBannerTests
{
    private sealed record Fixture(ChangesViewModel Changes, ExplainPanelViewModel Panel);

    private static Fixture NewFixture()
    {
        var runner = new GitRunner();
        var content = ContentLibrary.Load();
        var actions = new ActionService(runner, new RepoStateReader(runner), content);
        var setup = new SetupService(runner, new FolderInspector(), content);
        var panel = new ExplainPanelViewModel(
            actions, new StubConfirmationDialog(), new InMemorySettingsStore(), setup);
        return new Fixture(new ChangesViewModel(panel), panel);
    }

    private static RepoState State(string root = @"C:\r") => new(
        RepoRoot: root, Branch: "main", IsDetached: false, Upstream: null,
        Ahead: 0, Behind: 0, HasCommits: true, HasRemote: false,
        Changes: Array.Empty<FileChange>(),
        RecentCommits: Array.Empty<CommitInfo>(),
        Branches: Array.Empty<BranchInfo>());

    private static FolderState Folder(bool hasGitignore, string root = @"C:\r")
        => new(root, IsRepository: true, FileCount: 3, HasGitignore: hasGitignore, ProjectType.DotNet);

    [Fact]
    public void TheBannerAppearsWhenThereIsNoGitignore()
    {
        var f = NewFixture();

        f.Changes.Update(State(), Folder(hasGitignore: false));

        Assert.True(f.Changes.HasGitignoreOffer);
    }

    [Fact]
    public void TheBannerStaysHiddenWhenAGitignoreExists()
    {
        var f = NewFixture();

        f.Changes.Update(State(), Folder(hasGitignore: true));

        Assert.False(f.Changes.HasGitignoreOffer);
    }

    [Fact]
    public void TheBannerStaysHiddenWithoutFolderInformation()
    {
        var f = NewFixture();

        f.Changes.Update(State(), folder: null);

        Assert.False(f.Changes.HasGitignoreOffer);
    }

    [Fact]
    public async Task TheBannerPreviewsTheGitignoreOperation()
    {
        using var repo = await TestRepo.CreateAsync();
        File.WriteAllText(Path.Combine(repo.Path, "App.csproj"), "<Project />");
        var f = NewFixture();
        var reader = new RepoStateReader(new GitRunner());
        f.Changes.Update(
            await reader.ReadAsync(repo.Path), new FolderInspector().Inspect(repo.Path));

        await f.Changes.CreateGitignoreCommand.ExecuteAsync(null);

        Assert.Equal("Set up a .gitignore", f.Panel.Title);
        Assert.True(f.Panel.HasFileContents);
        // Previewed only: nothing is written until the user confirms.
        Assert.False(File.Exists(Path.Combine(repo.Path, ".gitignore")));
    }
}
