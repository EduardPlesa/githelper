using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GitHelper.App.Infrastructure;
using GitHelper.App.Settings;
using GitHelper.App.ViewModels;
using GitHelper.App.Views;
using GitHelper.Core.Actions;
using GitHelper.Core.Content;
using GitHelper.Core.Git;
using GitHelper.Core.Repo;

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

        return new MainViewModel(
            reader,
            new StartupViewModel(
                settings, new StubFolderPicker(), reader, new GitEnvironment(runner), new FolderInspector()),
            explain,
            new CommandLogViewModel(log, dispatcher),
            new ChangesViewModel(explain),
            new HistoryViewModel(explain),
            new BranchesViewModel(explain),
            new RepoWatcher(TimeSpan.FromMilliseconds(50), () => { }),
            new ThemeController(),
            settings,
            dispatcher);
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
}
