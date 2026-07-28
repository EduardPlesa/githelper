using GitHelper.App.Infrastructure;
using GitHelper.App.Settings;
using GitHelper.App.ViewModels;
using GitHelper.Core.Actions;
using GitHelper.Core.Content;
using GitHelper.Core.Git;
using GitHelper.Core.Repo;

namespace GitHelper.App.Tests;

public class MainViewModelTests
{
    private sealed record Fixture(
        MainViewModel Main,
        InMemorySettingsStore Settings,
        StubFolderPicker Picker,
        CommandLog Log);

    private static Fixture NewFixture()
    {
        var log = new CommandLog();
        var runner = new LoggingGitRunner(new GitRunner(), log);
        var reader = new RepoStateReader(runner);
        var service = new ActionService(runner, reader, ContentLibrary.Load());
        var settings = new InMemorySettingsStore();
        var picker = new StubFolderPicker();
        var dispatcher = new StubDispatcher();

        var explain = new ExplainPanelViewModel(service, new StubConfirmationDialog(), settings);
        var startup = new StartupViewModel(
            settings, picker, reader, new GitEnvironment(runner), new FolderInspector());

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

        return new Fixture(main, settings, picker, log);
    }

    [Fact]
    public void StartsWithNoRepositoryOpen()
    {
        using var f = NewFixture().Main;

        Assert.False(f.IsRepositoryOpen);
        Assert.Equal(MainTab.Changes, f.SelectedTab);
    }

    [Fact]
    public async Task OpeningARepositoryFromStartupPopulatesEveryTab()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        var f = NewFixture();
        using var main = f.Main;

        await main.Startup.OpenAsync(repo.Path);

        Assert.True(main.IsRepositoryOpen);
        Assert.Equal(Path.GetFileName(repo.Path), main.RepositoryName);
        Assert.Equal("main", main.BranchLabel);
        Assert.Single(main.Changes.Unstaged);
        Assert.Single(main.History.Commits);
        Assert.Single(main.Branches.Branches);
    }

    [Fact]
    public async Task RefreshAsync_PicksUpChangesMadeOutsideTheApp()
    {
        using var repo = await TestRepo.CreateAsync();
        var f = NewFixture();
        using var main = f.Main;
        await main.Startup.OpenAsync(repo.Path);
        Assert.Empty(main.Changes.Unstaged);

        repo.WriteFile("appeared.txt", "x\n");
        await main.RefreshAsync();

        Assert.Single(main.Changes.Unstaged);
    }

    [Fact]
    public async Task RunningAnActionRefreshesTheTabsAutomatically()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        var f = NewFixture();
        using var main = f.Main;
        await main.Startup.OpenAsync(repo.Path);

        // stage-file is Safe, so this runs immediately and must trigger a refresh.
        await main.Changes.Unstaged.Single().StageCommand.ExecuteAsync(null);

        Assert.Single(main.Changes.Staged);
        Assert.Empty(main.Changes.Unstaged);
    }

    [Fact]
    public async Task RunningAnActionSurfacesItsNarrationAsAStatusMessage()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        var f = NewFixture();
        using var main = f.Main;
        await main.Startup.OpenAsync(repo.Path);

        await main.Changes.Unstaged.Single().StageCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrWhiteSpace(main.StatusMessage));
    }

    [Fact]
    public async Task ConcurrentRefreshesDoNotCorruptTheBoundCollections()
    {
        // An action completing and the watcher firing can both request a refresh at once.
        // Before these were serialized, the two appends into the command log's
        // ObservableCollection raced and threw IndexOutOfRangeException.
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        var f = NewFixture();
        using var main = f.Main;
        await main.Startup.OpenAsync(repo.Path);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => main.RefreshAsync()));

        // Every recorded command landed exactly once, and the tabs reflect one coherent
        // snapshot rather than an interleaving of several.
        Assert.Equal(main.CommandLog.Entries.Count, main.CommandLog.Entries.Distinct().Count());
        Assert.Single(main.Changes.Unstaged);

        // The collection-corruption assertions above only manifest probabilistically; this
        // asserts the gate's actual invariant directly and deterministically.
        Assert.Equal(1, main.PeakConcurrentRefreshes);
    }

    [Fact]
    public async Task DisposeDoesNotStrandARefreshQueuedOnTheGate()
    {
        // Disposing a SemaphoreSlim out from under a parked WaitAsync leaves that caller
        // hanging forever with no exception, so a refresh queued at the moment of disposal
        // must be released rather than abandoned.
        using var repo = await TestRepo.CreateAsync();
        var f = NewFixture();
        var main = f.Main;
        await main.Startup.OpenAsync(repo.Path);

        // Several refreshes in flight, so at least one is very likely queued on the gate.
        var refreshes = Enumerable.Range(0, 8).Select(_ => main.RefreshAsync()).ToArray();
        main.Dispose();

        var all = Task.WhenAll(refreshes);
        var finished = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.Same(all, finished);

        // The only failure this test guards against is a hang (checked above); a queued
        // wait released by cancellation rather than by acquiring the gate is acceptable.
        try
        {
            await all;
        }
        catch (OperationCanceledException)
        {
        }
    }

    [Fact]
    public async Task CommittingClearsTheCommitBoxViaTheChangesViewModel()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        await repo.GitAsync("add", "-A");
        var f = NewFixture();
        using var main = f.Main;
        await main.Startup.OpenAsync(repo.Path);
        main.Changes.CommitMessage = "add a file";

        await main.Changes.CommitCommand.ExecuteAsync(null);
        await main.Explain.RunAsync();

        Assert.Equal(string.Empty, main.Changes.CommitMessage);
    }

    [Fact]
    public async Task OpeningARepositoryStartsRecordingCommandsInTheLog()
    {
        using var repo = await TestRepo.CreateAsync();
        var f = NewFixture();
        using var main = f.Main;

        await main.Startup.OpenAsync(repo.Path);

        // Reading state runs status, log, for-each-ref and remote.
        Assert.NotEmpty(main.CommandLog.Entries);
        Assert.Contains(main.CommandLog.Entries, e => e.CommandLine.StartsWith("git status"));
    }

    [Fact]
    public async Task BranchLabel_SaysSoInDetachedHead()
    {
        using var repo = await TestRepo.CreateAsync();
        var head = (await repo.GitAsync("rev-parse", "HEAD")).StdOut.Trim();
        await repo.GitAsync("checkout", "-q", head);
        var f = NewFixture();
        using var main = f.Main;

        await main.Startup.OpenAsync(repo.Path);

        Assert.Contains("detached", main.BranchLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InitializeAsync_AppliesTheSavedThemeChoice()
    {
        var f = NewFixture();
        using var main = f.Main;
        f.Settings.Current = AppSettings.Default.WithTheme(AppTheme.Dark);

        await main.InitializeAsync();

        Assert.Equal(AppTheme.Dark, main.CurrentTheme);
    }

    [Fact]
    public void CycleThemeCommand_WalksSystemThenDarkThenLightAndBack()
    {
        var f = NewFixture();
        using var main = f.Main;

        Assert.Equal(AppTheme.System, main.CurrentTheme);

        main.CycleThemeCommand.Execute(null);
        Assert.Equal(AppTheme.Dark, main.CurrentTheme);

        main.CycleThemeCommand.Execute(null);
        Assert.Equal(AppTheme.Light, main.CurrentTheme);

        main.CycleThemeCommand.Execute(null);
        Assert.Equal(AppTheme.System, main.CurrentTheme);
    }

    [Fact]
    public void CycleThemeCommand_PersistsTheChoice()
    {
        var f = NewFixture();
        using var main = f.Main;

        main.CycleThemeCommand.Execute(null);

        Assert.Equal(AppTheme.Dark, f.Settings.Current.Theme);
        Assert.True(f.Settings.SaveCount >= 1);
    }

    [Fact]
    public async Task CloseRepositoryCommand_ReturnsToTheStartupOverlay()
    {
        using var repo = await TestRepo.CreateAsync();
        var f = NewFixture();
        using var main = f.Main;
        await main.Startup.OpenAsync(repo.Path);
        Assert.True(main.IsRepositoryOpen);

        await main.CloseRepositoryCommand.ExecuteAsync(null);

        Assert.False(main.IsRepositoryOpen);
        Assert.Equal(ExplainPanelState.Empty, main.Explain.PanelState);
    }

    [Fact]
    public async Task SwitchingTabsDoesNotLoseRepositoryState()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        var f = NewFixture();
        using var main = f.Main;
        await main.Startup.OpenAsync(repo.Path);

        main.SelectedTab = MainTab.History;
        main.SelectedTab = MainTab.Changes;

        Assert.True(main.IsRepositoryOpen);
        Assert.Single(main.Changes.Unstaged);
    }
}
