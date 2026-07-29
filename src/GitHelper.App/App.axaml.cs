using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GitHelper.App.Infrastructure;
using GitHelper.App.Settings;
using GitHelper.App.ViewModels;
using GitHelper.App.Views;
using GitHelper.Core.Actions;
using GitHelper.Core.Content;
using GitHelper.Core.Git;
using GitHelper.Core.Repo;
using GitHelper.Core.Setup;

namespace GitHelper.App;

public partial class App : Application
{
    /// <summary>Matches the spec: one refresh per quiet period, not per filesystem event.</summary>
    private static readonly TimeSpan RefreshDebounce = TimeSpan.FromMilliseconds(500);

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            window.DataContext = BuildMainViewModel(() => window);
            desktop.MainWindow = window;

            // Started after the window exists, because the environment check and the folder
            // picker both need a top level to parent to.
            if (window.DataContext is MainViewModel main) _ = main.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// The only place concrete infrastructure is constructed. Everything else takes
    /// interfaces, which is what keeps the viewmodels testable without a window.
    /// </summary>
    private static MainViewModel BuildMainViewModel(Func<Window?> windowAccessor)
    {
        // LoggingGitRunner wraps GitRunner so the command log captures every invocation,
        // including the startup environment checks.
        var commandLog = new CommandLog();
        var runner = new LoggingGitRunner(new GitRunner(), commandLog);

        var reader = new RepoStateReader(runner);
        var content = ContentLibrary.Load();
        var actions = new ActionService(runner, reader, content);
        var environment = new GitEnvironment(runner);
        var inspector = new FolderInspector();
        var setupService = new SetupService(runner, inspector, content);

        var settings = new JsonSettingsStore(JsonSettingsStore.DefaultFilePath);
        var dispatcher = new AvaloniaUiDispatcher();
        var picker = new StorageFolderPicker(windowAccessor);
        var confirmations = new AvaloniaConfirmationDialog(windowAccessor);
        var browser = new ShellBrowserLauncher();

        var explain = new ExplainPanelViewModel(actions, confirmations, settings, setupService);
        var startup = new StartupViewModel(settings, picker, reader, environment, inspector);

        return new MainViewModel(
            reader,
            startup,
            explain,
            new CommandLogViewModel(commandLog, dispatcher),
            new ChangesViewModel(explain, browser),
            new HistoryViewModel(explain),
            new BranchesViewModel(explain),
            new RepoWatcher(RefreshDebounce, () => { }),
            new ThemeController(),
            settings,
            dispatcher,
            inspector);
    }
}
