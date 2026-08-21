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
        var runner = new SerializedGitRunner(new LoggingGitRunner(new GitRunner(), log));
        var reader = new RepoStateReader(runner);
        var service = new ActionService(runner, reader, ContentLibrary.Load());
        var settings = new InMemorySettingsStore();
        var picker = new StubFolderPicker();
        var dispatcher = new StubDispatcher();

        var explain = new ExplainPanelViewModel(service, new StubConfirmationDialog(), settings);
        var inspector = new FolderInspector();
        var startup = new StartupViewModel(
            settings, picker, reader, new GitEnvironment(runner), inspector);

        var main = new MainViewModel(
            reader,
            startup,
            explain,
            new CommandLogViewModel(log, dispatcher),
            new ChangesViewModel(explain),
            new DiffViewModel(new GitDiffSource(new DiffReader(runner)), TestContent.Library),
            new HistoryViewModel(explain),
            new BranchesViewModel(explain),
            new OperationBannerViewModel(explain),
            new RepoWatcher(TimeSpan.FromMilliseconds(50), () => { }),
            new ThemeController(),
            settings,
            dispatcher,
            inspector);

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

    [Fact]
    public async Task OpeningARepositoryMidMergeShowsTheBandStraightAway()
    {
        // The repository is the source of truth, not anything this app remembered. A merge
        // left running before the app was last closed has to be visible on opening.
        using var repo = await TestRepo.CreateAsync();
        await repo.StartConflictingMergeAsync();
        var f = NewFixture();
        using var main = f.Main;

        await main.Startup.OpenAsync(repo.Path);

        Assert.True(main.OperationBanner.IsVisible);
        Assert.Contains("feature", main.OperationBanner.Headline);
    }

    [Fact]
    public async Task TheBandStaysVisibleAcrossTabs()
    {
        using var repo = await TestRepo.CreateAsync();
        await repo.StartConflictingMergeAsync();
        var f = NewFixture();
        using var main = f.Main;
        await main.Startup.OpenAsync(repo.Path);

        main.SelectedTab = MainTab.Branches;

        Assert.True(main.OperationBanner.IsVisible);
    }

    [Fact]
    public async Task TheBandDisappearsWhenTheMergeIsAbandoned()
    {
        using var repo = await TestRepo.CreateAsync();
        await repo.StartConflictingMergeAsync();
        var f = NewFixture();
        using var main = f.Main;
        await main.Startup.OpenAsync(repo.Path);
        Assert.True(main.OperationBanner.IsVisible);

        await repo.GitAsync("merge", "--abort");
        await main.RefreshAsync();

        Assert.False(main.OperationBanner.IsVisible);
    }

    [Fact]
    public async Task ShowsTheDiffInPlaceOfTheCurrentTabWhenARowAsksForIt()
    {
        using var repo = await TestRepo.CreateAsync();
        var path = await repo.AddUnstagedChangeAsync();
        using var viewModel = NewFixture().Main;
        await viewModel.Startup.OpenAsync(repo.Path);

        await viewModel.Changes.Unstaged.Single(r => r.Path == path).ViewChangesCommand.ExecuteAsync(null);

        Assert.IsType<DiffViewModel>(viewModel.CurrentTab);
        Assert.Equal(path, ((DiffViewModel)viewModel.CurrentTab).Path);
    }

    [Fact]
    public async Task GoingBackRestoresTheTab()
    {
        using var repo = await TestRepo.CreateAsync();
        var path = await repo.AddUnstagedChangeAsync();
        using var viewModel = NewFixture().Main;
        await viewModel.Startup.OpenAsync(repo.Path);
        await viewModel.Changes.Unstaged.Single(r => r.Path == path).ViewChangesCommand.ExecuteAsync(null);

        ((DiffViewModel)viewModel.CurrentTab).CloseCommand.Execute(null);

        Assert.Same(viewModel.Changes, viewModel.CurrentTab);
    }

    /// <summary>
    /// The diff is about one file in one tab. Leaving that tab is leaving the question.
    /// </summary>
    [Fact]
    public async Task LeavingTheChangesTabClosesTheDiff()
    {
        using var repo = await TestRepo.CreateAsync();
        var path = await repo.AddUnstagedChangeAsync();
        using var viewModel = NewFixture().Main;
        await viewModel.Startup.OpenAsync(repo.Path);
        await viewModel.Changes.Unstaged.Single(r => r.Path == path).ViewChangesCommand.ExecuteAsync(null);

        viewModel.SelectedTab = MainTab.History;

        Assert.Same(viewModel.History, viewModel.CurrentTab);

        viewModel.SelectedTab = MainTab.Changes;

        Assert.Same(viewModel.Changes, viewModel.CurrentTab);
    }

    /// <summary>Conflicted files need combined diff format, which is v3's, not this surface's.</summary>
    [Fact]
    public async Task OffersNoDiffForAConflictedFile()
    {
        using var repo = await TestRepo.CreateAsync();
        await repo.StartConflictingMergeAsync();
        using var viewModel = NewFixture().Main;
        await viewModel.Startup.OpenAsync(repo.Path);

        Assert.NotEmpty(viewModel.Changes.Conflicted);
        Assert.All(viewModel.Changes.Conflicted, row => Assert.False(row.CanViewChanges));
    }

    /// <summary>
    /// An open diff belongs to the repository it was read from. Closing that repository has
    /// to take it with it, or the next project opened shows the last one's file.
    /// </summary>
    [Fact]
    public async Task ClosingTheRepositoryClosesAnOpenDiff()
    {
        using var repo = await TestRepo.CreateAsync();
        var path = await repo.AddUnstagedChangeAsync();
        using var viewModel = NewFixture().Main;
        await viewModel.Startup.OpenAsync(repo.Path);
        await viewModel.Changes.Unstaged.Single(r => r.Path == path).ViewChangesCommand.ExecuteAsync(null);

        await viewModel.CloseRepositoryCommand.ExecuteAsync(null);

        Assert.Null(viewModel.OpenDiff);
    }

    /// <summary>
    /// A diff read once and left alone would quietly disagree with the file list beside it.
    /// The file's status does not change when it is edited twice, so nothing but re-reading
    /// catches this.
    /// </summary>
    [Fact]
    public async Task RefreshingReReadsAnOpenDiff()
    {
        using var repo = await TestRepo.CreateAsync();
        var path = await repo.AddUnstagedChangeAsync();
        using var viewModel = NewFixture().Main;
        await viewModel.Startup.OpenAsync(repo.Path);
        await viewModel.Changes.Unstaged.Single(r => r.Path == path).ViewChangesCommand.ExecuteAsync(null);

        repo.WriteFile(path, "one\nEDITED AGAIN\nthree\n");
        await viewModel.RefreshAsync();

        var lines = viewModel.Diff.Hunks.SelectMany(h => h.Lines).ToList();
        Assert.Contains(lines, l => l.Text == "EDITED AGAIN");
        Assert.DoesNotContain(lines, l => l.Text == "TWO");
    }
}
