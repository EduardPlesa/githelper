using GitHelper.App.Infrastructure;
using GitHelper.App.ViewModels;
using GitHelper.Core.Actions;
using GitHelper.Core.Content;
using GitHelper.Core.Git;
using GitHelper.Core.Repo;

namespace GitHelper.App.Tests;

/// <summary>
/// A freshly initialised repository with no commits, driven end to end through the shell.
///
/// This is the state a beginner is in the very first time they open the app on a new project,
/// and it is the one where git is least forgiving: there is no HEAD, so no branch ref exists
/// yet, `git log` fails rather than returning nothing, and `restore --staged` has nothing to
/// restore from. The pieces are unit-tested individually; what is covered here is that the
/// whole first-commit journey works, which is the part a user actually performs.
/// </summary>
public class EmptyRepositoryTests
{
    private sealed record Fixture(MainViewModel Main, RepoStateReader Reader);

    private static Fixture NewFixture()
    {
        var log = new CommandLog();
        var runner = new LoggingGitRunner(new GitRunner(), log);
        var reader = new RepoStateReader(runner);
        var service = new ActionService(runner, reader, ContentLibrary.Load());
        var settings = new InMemorySettingsStore();
        var dispatcher = new StubDispatcher();

        var explain = new ExplainPanelViewModel(service, new StubConfirmationDialog(), settings);
        var startup = new StartupViewModel(
            settings, new StubFolderPicker(), reader, new GitEnvironment(runner), new FolderInspector());

        var main = new MainViewModel(
            reader,
            startup,
            explain,
            new CommandLogViewModel(log, dispatcher),
            new ChangesViewModel(explain),
            new HistoryViewModel(explain),
            new BranchesViewModel(explain),
            new RepoWatcher(TimeSpan.FromMilliseconds(50), () => { }),
            new ThemeController(),
            settings,
            dispatcher);

        return new Fixture(main, reader);
    }

    [Fact]
    public async Task OpensWithEmptyListsRatherThanAnError()
    {
        using var repo = await TestRepo.CreateAsync(withInitialCommit: false);
        var f = NewFixture();
        using var main = f.Main;

        await main.Startup.OpenAsync(repo.Path);

        Assert.True(main.IsRepositoryOpen);
        Assert.Empty(main.History.Commits);
        Assert.False(main.History.HasCommits);
        Assert.False(main.Changes.HasAnyChanges);
        // No branch ref exists until the first commit, but the app must still name where it is
        // rather than showing a blank or claiming a detached HEAD.
        Assert.False(string.IsNullOrWhiteSpace(main.BranchLabel));
    }

    [Fact]
    public async Task AnUntrackedFileCanBeStagedBeforeAnyCommitExists()
    {
        using var repo = await TestRepo.CreateAsync(withInitialCommit: false);
        var f = NewFixture();
        using var main = f.Main;
        await main.Startup.OpenAsync(repo.Path);

        repo.WriteFile("first.txt", "hello\n");
        await main.RefreshAsync();

        var row = Assert.Single(main.Changes.Unstaged);
        await row.StageCommand.ExecuteAsync(null);

        Assert.Single(main.Changes.Staged);
        Assert.Empty(main.Changes.Unstaged);
    }

    [Fact]
    public async Task TheFirstCommitSucceedsAndAppearsInHistory()
    {
        using var repo = await TestRepo.CreateAsync(withInitialCommit: false);
        var f = NewFixture();
        using var main = f.Main;
        await main.Startup.OpenAsync(repo.Path);

        repo.WriteFile("first.txt", "hello\n");
        await main.RefreshAsync();
        await main.Changes.Unstaged.Single().StageCommand.ExecuteAsync(null);

        main.Changes.CommitMessage = "my first commit";
        await main.Changes.CommitCommand.ExecuteAsync(null);
        // Commit is a Caution action, so it waits for confirmation rather than running outright.
        await main.Explain.RunAsync();

        Assert.True(main.History.HasCommits);
        var commit = Assert.Single(main.History.Commits);
        Assert.Equal("my first commit", commit.Subject);
        Assert.Empty(main.Changes.Staged);
    }

    [Fact]
    public async Task UndoIsRefusedOnTheFirstCommitBecauseThereIsNoParent()
    {
        // reset --soft HEAD~1 has no parent to reset to here. The app must explain that rather
        // than offering a button that would fail.
        using var repo = await TestRepo.CreateAsync(withInitialCommit: false);
        repo.WriteFile("first.txt", "hello\n");
        await repo.GitAsync("add", "-A");
        await repo.GitAsync("commit", "-q", "-m", "only commit");

        var f = NewFixture();
        using var main = f.Main;
        await main.Startup.OpenAsync(repo.Path);

        Assert.False(Assert.Single(main.History.Commits).CanUndo);
    }
}
