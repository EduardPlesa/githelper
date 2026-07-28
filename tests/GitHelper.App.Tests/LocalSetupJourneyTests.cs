using GitHelper.App.Infrastructure;
using GitHelper.App.ViewModels;
using GitHelper.Core.Actions;
using GitHelper.Core.Content;
using GitHelper.Core.Git;
using GitHelper.Core.Repo;
using GitHelper.Core.Setup;

namespace GitHelper.App.Tests;

public class LocalSetupJourneyTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "githelper-journey-" + Guid.NewGuid().ToString("N"));

    public LocalSetupJourneyTests() => Directory.CreateDirectory(_dir);

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

    private static MainViewModel NewMain() => NewMain(out _);

    private static MainViewModel NewMain(out InMemorySettingsStore settings)
    {
        var log = new CommandLog();
        var runner = new LoggingGitRunner(new GitRunner(), log);
        var reader = new RepoStateReader(runner);
        var content = ContentLibrary.Load();
        var actions = new ActionService(runner, reader, content);
        var inspector = new FolderInspector();
        var setup = new SetupService(runner, inspector, content);
        settings = new InMemorySettingsStore();
        var dispatcher = new StubDispatcher();
        var explain = new ExplainPanelViewModel(
            actions, new StubConfirmationDialog(), settings, setup);

        return new MainViewModel(
            reader,
            new StartupViewModel(settings, new StubFolderPicker(), reader,
                new GitEnvironment(runner), inspector),
            explain,
            new CommandLogViewModel(log, dispatcher),
            new ChangesViewModel(explain),
            new HistoryViewModel(explain),
            new BranchesViewModel(explain),
            new RepoWatcher(TimeSpan.FromMilliseconds(50), () => { }),
            new ThemeController(),
            settings,
            dispatcher,
            inspector);
    }

    [Fact]
    public async Task ANewlyCreatedRepositoryIsRememberedAsARecentProject()
    {
        // Creating a repository used to open it without ever recording it, so the one project
        // the user had just made was missing from Recent projects on the next launch.
        using var main = NewMain(out var settings);

        await main.Startup.OpenAsync(_dir);
        await main.Startup.StartTrackingCommand.ExecuteAsync(null);
        await main.Explain.ConfirmCommand.ExecuteAsync(null);

        Assert.True(main.IsRepositoryOpen);

        // Compared by folder name: git resolves the repository root itself, and on Windows the
        // temp path it returns can differ from _dir in case and separators.
        var expected = Path.GetFileName(_dir);
        Assert.Contains(
            settings.Current.RecentRepositories,
            p => string.Equals(Path.GetFileName(p.TrimEnd('/', '\\')), expected, StringComparison.Ordinal));
        Assert.NotEmpty(main.Startup.Recents);
    }

    [Fact]
    public async Task AFolderBecomesATrackedProjectWithAFirstCommit()
    {
        File.WriteAllText(Path.Combine(_dir, "App.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(_dir, "Program.cs"), "// hi");
        using var main = NewMain();

        // 1. Choosing the folder offers to create a repository.
        await main.Startup.OpenAsync(_dir);
        Assert.True(main.Startup.IsOfferingInit);
        Assert.False(main.IsRepositoryOpen);

        // 2. Accepting previews init, then confirming runs it and opens the project.
        // Driven through ConfirmCommand rather than RunSetupAsync directly: that ternary is
        // the seam that once let a stale setup run behind an action preview, so a journey
        // test that bypasses it could stay green while that regression came back.
        await main.Startup.StartTrackingCommand.ExecuteAsync(null);
        Assert.Equal("Start tracking this folder", main.Explain.Title);
        await main.Explain.ConfirmCommand.ExecuteAsync(null);

        Assert.True(main.IsRepositoryOpen);
        Assert.True(Directory.Exists(Path.Combine(_dir, ".git")));

        // git init does not set a user identity, and the journey below commits — without
        // this the commit step fails with "Please tell me who you are".
        await new GitRunner().RunAsync(_dir, new[] { "config", "user.name", "Test User" });
        await new GitRunner().RunAsync(_dir, new[] { "config", "user.email", "test@example.com" });
        await new GitRunner().RunAsync(_dir, new[] { "config", "commit.gpgsign", "false" });

        // 3. The .gitignore banner is offered, and writing it uses the .NET template.
        Assert.True(main.Changes.HasGitignoreOffer);
        await main.Changes.CreateGitignoreCommand.ExecuteAsync(null);
        await main.Explain.ConfirmCommand.ExecuteAsync(null);
        Assert.Contains("obj/", await File.ReadAllTextAsync(Path.Combine(_dir, ".gitignore")));

        // 4. Staging and committing work as they do in any other project.
        await main.RefreshAsync();
        Assert.False(main.Changes.HasGitignoreOffer);
        await main.Changes.StageAllCommand.ExecuteAsync(null);
        main.Changes.CommitMessage = "first commit";
        await main.Changes.CommitCommand.ExecuteAsync(null);
        await main.Explain.ConfirmCommand.ExecuteAsync(null);

        Assert.True(main.History.HasCommits);
        Assert.Equal("first commit", main.History.Commits.Single().Subject);
    }

    [Fact]
    public async Task ASuccessfulInitLeavesTheNarrationVisibleAfterOpening()
    {
        using var main = NewMain();
        await main.Startup.OpenAsync(_dir);
        await main.Startup.StartTrackingCommand.ExecuteAsync(null);

        await main.Explain.ConfirmCommand.ExecuteAsync(null);

        // OpenRepositoryAsync clears StatusMessage as part of its normal job, so the
        // narration must be set after that clear, not before it — otherwise a successful
        // `git init` tells the user nothing happened.
        Assert.True(main.IsRepositoryOpen);
        Assert.False(string.IsNullOrWhiteSpace(main.StatusMessage));
    }

    [Fact]
    public async Task CancellingABlockedInitReturnsToTheOrdinaryChooser()
    {
        using var main = NewMain();
        await main.Startup.OpenAsync(_dir);
        await main.Startup.StartTrackingCommand.ExecuteAsync(null);
        Assert.True(main.Explain.IsShowingSetup);

        // Something else creates the repository before the user confirms, so the setup
        // they are about to run is now blocked.
        await new GitRunner().RunAsync(_dir, new[] { "init", "-q", "-b", "main" });
        await main.Explain.ConfirmCommand.ExecuteAsync(null);
        Assert.True(main.Explain.HasBlockers);
        Assert.True(main.Explain.IsShowingSetup);
        Assert.False(main.IsRepositoryOpen);

        // Without a way back here, the only escape from behind the scrim is restarting
        // the app.
        main.Explain.CancelSetupCommand.Execute(null);

        Assert.False(main.Explain.IsShowingSetup);
        Assert.Equal(StartupState.AwaitingChoice, main.Startup.State);
        Assert.Null(main.Startup.PendingFolder);
    }
}
