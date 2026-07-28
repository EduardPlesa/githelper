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

    private static MainViewModel NewMain()
    {
        var log = new CommandLog();
        var runner = new LoggingGitRunner(new GitRunner(), log);
        var reader = new RepoStateReader(runner);
        var content = ContentLibrary.Load();
        var actions = new ActionService(runner, reader, content);
        var inspector = new FolderInspector();
        var setup = new SetupService(runner, inspector, content);
        var settings = new InMemorySettingsStore();
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
        await main.Startup.StartTrackingCommand.ExecuteAsync(null);
        Assert.Equal("Start tracking this folder", main.Explain.Title);
        await main.Explain.RunSetupAsync();

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
        await main.Explain.RunSetupAsync();
        Assert.Contains("obj/", await File.ReadAllTextAsync(Path.Combine(_dir, ".gitignore")));

        // 4. Staging and committing work as they do in any other project.
        await main.RefreshAsync();
        Assert.False(main.Changes.HasGitignoreOffer);
        await main.Changes.StageAllCommand.ExecuteAsync(null);
        main.Changes.CommitMessage = "first commit";
        await main.Changes.CommitCommand.ExecuteAsync(null);
        await main.Explain.RunAsync();

        Assert.True(main.History.HasCommits);
        Assert.Equal("first commit", main.History.Commits.Single().Subject);
    }
}
