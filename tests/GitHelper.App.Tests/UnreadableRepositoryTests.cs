using GitHelper.App.Infrastructure;
using GitHelper.App.ViewModels;
using GitHelper.Core.Actions;
using GitHelper.Core.Content;
using GitHelper.Core.Git;
using GitHelper.Core.Repo;

namespace GitHelper.App.Tests;

/// <summary>
/// What the app does when a read it depends on stops working underneath it — the repository
/// deleted mid-session, a permissions change, an index git cannot parse.
///
/// The reader refuses to invent a state in that case, which means the UI has to cope with
/// being told "I don't know" rather than being handed a plausible-looking empty project.
/// </summary>
public class UnreadableRepositoryTests
{
    /// <summary>Real git until <see cref="Break"/> is set, then fails every status read.</summary>
    private sealed class BreakableRunner : IGitRunner
    {
        private readonly GitRunner _real = new();

        public bool Break { get; set; }

        public Task<GitCommandResult> RunAsync(
            string workingDirectory, IReadOnlyList<string> args, CancellationToken ct = default)
        {
            if (Break && args.Count > 0 && args[0] == "status")
            {
                return Task.FromResult(new GitCommandResult(
                    args, string.Empty, "fatal: unable to read index", 128, TimeSpan.Zero));
            }

            return _real.RunAsync(workingDirectory, args, ct);
        }
    }

    private static MainViewModel NewMain(IGitRunner runner, out ExplainPanelViewModel explain)
    {
        var reader = new RepoStateReader(runner);
        var settings = new InMemorySettingsStore();
        var dispatcher = new StubDispatcher();
        var inspector = new FolderInspector();

        explain = new ExplainPanelViewModel(
            new ActionService(runner, reader, ContentLibrary.Load()),
            new StubConfirmationDialog(),
            settings);

        return new MainViewModel(
            reader,
            new StartupViewModel(
                settings, new StubFolderPicker(), reader, new GitEnvironment(runner), inspector),
            explain,
            new CommandLogViewModel(new CommandLog(), dispatcher),
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
    }

    [Fact]
    public async Task ARefreshThatCannotReadTheRepositorySaysSoInsteadOfThrowing()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        var runner = new BreakableRunner();
        using var main = NewMain(runner, out _);
        await main.Startup.OpenAsync(repo.Path);
        Assert.Single(main.Changes.Unstaged);

        runner.Break = true;
        await main.RefreshAsync();

        Assert.False(string.IsNullOrWhiteSpace(main.StatusMessage));
        Assert.Contains("read", main.StatusMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ARefreshThatCannotReadKeepsTheLastKnownStateRatherThanBlankingIt()
    {
        // Showing an empty project would be a lie, and the most alarming possible lie: the
        // user's work appears to have vanished.
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        var runner = new BreakableRunner();
        using var main = NewMain(runner, out _);
        await main.Startup.OpenAsync(repo.Path);

        runner.Break = true;
        await main.RefreshAsync();

        Assert.Single(main.Changes.Unstaged);
        Assert.True(main.IsRepositoryOpen);
    }

    [Fact]
    public async Task PreviewingAnActionThatCannotReadTheRepositoryShowsAnError()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        var runner = new BreakableRunner();
        using var main = NewMain(runner, out var explain);
        await main.Startup.OpenAsync(repo.Path);

        runner.Break = true;
        await explain.ShowAsync(repo.Path, new ActionRequest("stage-all"));

        Assert.Equal(ExplainPanelState.Error, explain.PanelState);
        Assert.NotNull(explain.Error);
        Assert.False(explain.CanRun);
    }

    [Fact]
    public async Task RunningAnActionThatCannotReadTheRepositoryShowsAnErrorRatherThanRunning()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        var runner = new BreakableRunner();
        using var main = NewMain(runner, out var explain);
        await main.Startup.OpenAsync(repo.Path);

        await explain.ShowAsync(repo.Path, new ActionRequest("stage-all"));
        runner.Break = true;
        await explain.RunAsync();

        Assert.Equal(ExplainPanelState.Error, explain.PanelState);
        Assert.NotNull(explain.Error);

        // Nothing was staged: the action must not run against a state it could not read.
        var staged = await repo.GitAsync("diff", "--cached", "--name-only");
        Assert.Empty(staged.StdOut.Trim());
    }
}
