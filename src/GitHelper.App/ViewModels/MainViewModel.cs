using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHelper.App.Infrastructure;
using GitHelper.App.Settings;
using GitHelper.Core.Actions;
using GitHelper.Core.Repo;

namespace GitHelper.App.ViewModels;

public enum MainTab
{
    Changes,
    History,
    Branches,
}

/// <summary>
/// The shell. The only class that knows how the pieces fit together: it opens
/// repositories, refreshes state after actions and after on-disk changes, and owns the
/// theme. No child viewmodel knows about its siblings.
/// </summary>
public sealed partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly RepoStateReader _reader;
    private readonly RepoWatcher _watcher;
    private readonly ThemeController _themeController;
    private readonly ISettingsStore _settings;
    private readonly IUiDispatcher _dispatcher;

    private string? _repoPath;

    public MainViewModel(
        RepoStateReader reader,
        StartupViewModel startup,
        ExplainPanelViewModel explain,
        CommandLogViewModel commandLog,
        ChangesViewModel changes,
        HistoryViewModel history,
        BranchesViewModel branches,
        RepoWatcher watcher,
        ThemeController themeController,
        ISettingsStore settings,
        IUiDispatcher dispatcher)
    {
        _reader = reader;
        _watcher = watcher;
        _themeController = themeController;
        _settings = settings;
        _dispatcher = dispatcher;

        Startup = startup;
        Explain = explain;
        CommandLog = commandLog;
        Changes = changes;
        History = history;
        Branches = branches;

        CycleThemeCommand = new RelayCommand(CycleTheme);
        CloseRepositoryCommand = new AsyncRelayCommand(CloseRepositoryAsync);

        Startup.RepositoryOpened += OnRepositoryOpened;
        Explain.ActionCompleted += OnActionCompleted;

        // Hop to the UI thread: the watcher fires on a thread-pool thread, and the refresh
        // rebuilds collections that are bound to controls.
        _watcher.OnChanged = () => _dispatcher.Post(() => _ = RefreshAsync());
    }

    public StartupViewModel Startup { get; }

    public ExplainPanelViewModel Explain { get; }

    public CommandLogViewModel CommandLog { get; }

    public ChangesViewModel Changes { get; }

    public HistoryViewModel History { get; }

    public BranchesViewModel Branches { get; }

    [ObservableProperty] private bool _isRepositoryOpen;
    [ObservableProperty] private string _repositoryName = string.Empty;
    [ObservableProperty] private string _branchLabel = string.Empty;
    [ObservableProperty] private MainTab _selectedTab = MainTab.Changes;
    [ObservableProperty] private AppTheme _currentTheme = AppTheme.System;
    [ObservableProperty] private string? _statusMessage;

    public IRelayCommand CycleThemeCommand { get; }

    public IAsyncRelayCommand CloseRepositoryCommand { get; }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        CurrentTheme = _settings.Load().Theme;
        _themeController.Apply(CurrentTheme);

        await Startup.InitializeAsync(ct);
    }

    /// <summary>Re-reads repository state and republishes it to every tab.</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (_repoPath is null) return;

        var state = await _reader.ReadAsync(_repoPath, ct);

        RepositoryName = new DirectoryInfo(state.RepoRoot).Name;
        BranchLabel = state.IsDetached
            ? "not on a branch (detached HEAD)"
            : state.Branch ?? "no branch";

        Changes.Update(state);
        History.Update(state);
        Branches.Update(state);
    }

    public void Dispose()
    {
        Startup.RepositoryOpened -= OnRepositoryOpened;
        Explain.ActionCompleted -= OnActionCompleted;
        _watcher.Dispose();
        CommandLog.Dispose();
    }

    private void OnRepositoryOpened(object? sender, string repoRoot)
        // StartupViewModel.RepositoryOpened is a plain synchronous event: OpenAsync raises
        // it as its very last statement and does not await subscribers, so a bare fire-and-
        // forget task here would leave IsRepositoryOpen/RepositoryName/the tabs unset for an
        // arbitrary stretch after OpenAsync's own Task has already completed. Task.Run moves
        // the work off whatever thread raised the event (including the UI thread) and clears
        // the captured SynchronizationContext for it, so blocking on it here cannot deadlock
        // even though the inner awaits (in RepoStateReader, out of this task's control) do
        // not use ConfigureAwait(false).
        => Task.Run(() => OpenRepositoryAsync(repoRoot)).GetAwaiter().GetResult();

    private async Task OpenRepositoryAsync(string repoRoot)
    {
        _repoPath = repoRoot;
        Explain.Clear();
        StatusMessage = null;

        await RefreshAsync();

        IsRepositoryOpen = true;

        // The watcher fires on a thread-pool thread, so the refresh it triggers has to hop
        // to the UI thread before rebuilding collections bound to controls.
        _watcher.Watch(repoRoot);
    }

    private void OnActionCompleted(object? sender, ActionOutcome outcome)
    {
        if (outcome.Narration is { Length: > 0 }) StatusMessage = outcome.Narration;

        Changes.OnActionCompleted(outcome);
        Branches.OnActionCompleted(outcome);

        // Same reasoning as OnRepositoryOpened: ExplainPanelViewModel.RunAsync raises
        // ActionCompleted synchronously and does not await subscribers, so the refresh is
        // pushed onto a context-free background task and waited on here rather than left to
        // race the caller of RunAsync.
        Task.Run(() => RefreshAsync()).GetAwaiter().GetResult();
    }

    private Task CloseRepositoryAsync()
    {
        _repoPath = null;
        IsRepositoryOpen = false;
        StatusMessage = null;

        Explain.Clear();
        _watcher.Stop();

        return Startup.InitializeAsync();
    }

    private void CycleTheme()
    {
        // Cycles rather than toggles so "follow the OS" stays reachable once touched.
        CurrentTheme = CurrentTheme switch
        {
            AppTheme.System => AppTheme.Dark,
            AppTheme.Dark => AppTheme.Light,
            _ => AppTheme.System,
        };

        _themeController.Apply(CurrentTheme);
        _settings.Save(_settings.Load().WithTheme(CurrentTheme));
    }
}
