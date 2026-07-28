using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitHelper.App.Infrastructure;
using GitHelper.App.Settings;
using GitHelper.App.ViewModels;
using GitHelper.App.Views;
using GitHelper.Core.Actions;
using GitHelper.Core.Content;
using GitHelper.Core.Git;
using GitHelper.Core.Repo;
using GitHelper.Core.Setup;

namespace GitHelper.App.Tests;

public class StartupViewModelFlagTests
{
    private static StartupViewModel NewStartup(out InMemorySettingsStore settings)
    {
        var runner = new GitRunner();
        settings = new InMemorySettingsStore();
        return new StartupViewModel(
            settings, new StubFolderPicker(), new RepoStateReader(runner), new GitEnvironment(runner),
            new FolderInspector());
    }

    [Fact]
    public async Task Flags_FollowTheState()
    {
        var startup = NewStartup(out _);

        Assert.True(startup.IsChecking);
        Assert.False(startup.IsAwaitingChoice);

        await startup.InitializeAsync();

        Assert.False(startup.IsChecking);
        Assert.True(startup.IsAwaitingChoice);
        Assert.False(startup.IsGitMissing);
    }

    [Fact]
    public async Task HasRecents_TracksTheLoadedList()
    {
        var startup = NewStartup(out var settings);
        settings.Current = AppSettings.Default.WithRepositoryOpened(@"C:\repos\demo");

        await startup.InitializeAsync();

        Assert.True(startup.HasRecents);
    }

    [Fact]
    public async Task OpeningANonRepositoryFolderOffersToStartTrackingInsteadOfErroring()
    {
        // A non-repository folder used to dead-end with an error message. It is now the entry
        // point for `git init`, so the offer flag is raised instead.
        var startup = NewStartup(out _);
        var dir = Path.Combine(Path.GetTempPath(), "githelper-notarepo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            await startup.OpenAsync(dir);

            Assert.True(startup.IsOfferingInit);
            Assert.NotNull(startup.PendingFolder);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

public class ShellTests
{
    private static MainViewModel NewMain()
    {
        var log = new CommandLog();
        var runner = new LoggingGitRunner(new GitRunner(), log);
        var reader = new RepoStateReader(runner);
        var service = new ActionService(runner, reader, ContentLibrary.Load());
        var settings = new InMemorySettingsStore();
        var dispatcher = new StubDispatcher();
        var explain = new ExplainPanelViewModel(service, new StubConfirmationDialog(), settings);
        var inspector = new FolderInspector();

        return new MainViewModel(
            reader,
            new StartupViewModel(
                settings, new StubFolderPicker(), reader, new GitEnvironment(runner), inspector),
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

    /// <summary>
    /// Like <see cref="NewMain"/>, but wired with a SetupService so the init-offer path
    /// (Explain.ShowSetupAsync) can actually be driven, rather than throwing
    /// InvalidOperationException the way a panel built without one does.
    /// </summary>
    private static MainViewModel NewMainWithSetup()
    {
        var log = new CommandLog();
        var runner = new LoggingGitRunner(new GitRunner(), log);
        var reader = new RepoStateReader(runner);
        var content = ContentLibrary.Load();
        var service = new ActionService(runner, reader, content);
        var inspector = new FolderInspector();
        var setup = new SetupService(runner, inspector, content);
        var settings = new InMemorySettingsStore();
        var dispatcher = new StubDispatcher();
        var explain = new ExplainPanelViewModel(service, new StubConfirmationDialog(), settings, setup);

        return new MainViewModel(
            reader,
            new StartupViewModel(
                settings, new StubFolderPicker(), reader, new GitEnvironment(runner), inspector),
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

    [AvaloniaFact]
    public void MainWindow_OpensWithTheShellChromeAndTheStartupScrim()
    {
        using var main = NewMain();
        var window = new MainWindow { DataContext = main };

        window.Show();

        Assert.True(window.IsVisible);
        // The chrome exists from the first frame, with the scrim on top of it.
        Assert.NotNull(window.FindControl<Panel>("StartupScrim"));
        Assert.NotNull(window.FindControl<ListBox>("TabList"));
        Assert.NotNull(window.FindControl<ContentControl>("TabContent"));
    }

    [AvaloniaFact]
    public void CurrentTab_FollowsTheSelectedTab()
    {
        using var main = NewMain();

        Assert.Same(main.Changes, main.CurrentTab);

        main.SelectedTab = MainTab.History;
        Assert.Same(main.History, main.CurrentTab);

        main.SelectedTab = MainTab.Branches;
        Assert.Same(main.Branches, main.CurrentTab);
    }

    [AvaloniaFact]
    public async Task MainWindow_SwapsTheCentrePaneWhenTheTabChanges()
    {
        using var repo = await TestRepo.CreateAsync();
        using var main = NewMain();
        var window = new MainWindow { DataContext = main };
        window.Show();
        await main.Startup.OpenAsync(repo.Path);

        var host = window.FindControl<ContentControl>("TabContent");
        Assert.NotNull(host);
        Assert.Same(main.Changes, host!.Content);

        main.SelectedTab = MainTab.Branches;

        Assert.Same(main.Branches, host.Content);
    }

    [AvaloniaFact]
    public void StartupOverlay_RendersWithoutAViewModel()
    {
        var window = new Window { Content = new StartupOverlay() };

        window.Show();

        Assert.True(window.IsVisible);
    }

    [AvaloniaFact]
    public async Task StartupOverlay_ShowsRecentRepositories()
    {
        var runner = new GitRunner();
        var settings = new InMemorySettingsStore
        {
            Current = AppSettings.Default.WithRepositoryOpened(@"C:\repos\demo"),
        };
        var startup = new StartupViewModel(
            settings, new StubFolderPicker(), new RepoStateReader(runner), new GitEnvironment(runner),
            new FolderInspector());
        await startup.InitializeAsync();

        var view = new StartupOverlay { DataContext = startup };
        var window = new Window { Content = view };
        window.Show();

        Assert.NotNull(view.FindControl<ItemsControl>("RecentsHost"));
        Assert.Single(startup.Recents);
    }

    [AvaloniaFact]
    public async Task Shell_HidesTheScrimOnceARepositoryIsOpen()
    {
        using var repo = await TestRepo.CreateAsync();
        using var main = NewMain();
        var window = new MainWindow { DataContext = main };
        window.Show();

        var scrim = window.FindControl<Panel>("StartupScrim");
        Assert.NotNull(scrim);
        Assert.True(scrim!.IsVisible);

        await main.Startup.OpenAsync(repo.Path);

        Assert.False(scrim.IsVisible);
    }

    [AvaloniaFact]
    public async Task StartupScrim_LeavesTheSetupConfirmButtonHitTestableRatherThanCoveringIt()
    {
        // Regression for a chicken-and-egg defect: IsRepositoryOpen (and so the scrim) only
        // flips once `git init` has actually run, and the only way to run it is clicking
        // Confirm on the init preview. If that preview is not raised above the scrim, Confirm
        // is visible in the layout but every point on it hit-tests to the scrim instead — the
        // user can see it and can never reach it. Bounds/visibility alone would not catch
        // that, so this drives an actual point-based hit test at the button's own location.
        var dir = Path.Combine(Path.GetTempPath(), "githelper-hittest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.txt"), "x");

        try
        {
            using var main = NewMainWithSetup();
            var window = new MainWindow { DataContext = main };
            window.Show();

            await main.Startup.OpenAsync(dir);
            Assert.True(main.Startup.IsOfferingInit);

            await main.Startup.StartTrackingCommand.ExecuteAsync(null);
            Assert.True(main.Explain.IsShowingSetup);
            Assert.True(main.Explain.RequiresInlineConfirmation);

            // Flush the queued layout pass so Bounds reflect the state above, not the first frame.
            Dispatcher.UIThread.RunJobs();

            // Explain is shared: ExplainPanelView is bound to it both behind the chrome (where
            // it has always lived) and, with the fix, inside the scrim — so two "ConfirmButton"
            // controls exist at once. That is fine; what matters is that at least one of them
            // is actually reachable where it renders, not merely present in the visual tree.
            var confirmButtons = window.GetVisualDescendants()
                .OfType<Button>()
                .Where(b => b.Name == "ConfirmButton")
                .ToList();
            Assert.NotEmpty(confirmButtons);

            var reachable = confirmButtons.Any(button =>
            {
                var localCenter = new Point(button.Bounds.Width / 2, button.Bounds.Height / 2);
                var pointInWindow = button.TranslatePoint(localCenter, window);
                if (pointInWindow is not { } point) return false;

                var hit = window.InputHitTest(point);
                return hit is Visual visual
                    && (ReferenceEquals(visual, button) || button.IsVisualAncestorOf(visual));
            });

            Assert.True(
                reachable,
                "Expected at least one Confirm button to hit-test to itself at the point where it "
                    + "renders, but every rendered copy was covered by something on top of it.");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
