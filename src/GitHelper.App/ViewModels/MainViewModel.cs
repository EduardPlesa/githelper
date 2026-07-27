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
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly CancellationTokenSource _disposing = new();
    private int _refreshesInFlight;

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

        Startup.RepositoryOpenedAsync = (repoRoot, ct) => OpenRepositoryAsync(repoRoot, ct);
        Explain.ActionCompletedAsync = OnActionCompletedAsync;

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

    /// <summary>
    /// The highest number of refreshes ever observed running at once. Exposed for tests:
    /// the gate's whole purpose is that this never exceeds one, and asserting that directly
    /// is deterministic, unlike waiting for a collection corruption that only manifests
    /// intermittently.
    /// </summary>
    internal int PeakConcurrentRefreshes { get; private set; }

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

        // An action completing and the file watcher waking can both ask for a refresh at
        // the same moment — running an action writes to .git, which is exactly what the
        // watcher is watching. Overlapping refreshes double the git work and can interleave
        // two different snapshots into the collections the views are bound to.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposing.Token);

        try
        {
            await _refreshGate.WaitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            // Disposed, or the caller cancelled, while queued. Disposing a SemaphoreSlim
            // out from under a parked WaitAsync leaves it hanging forever with no
            // exception, so the wait must be cancellable rather than simply abandoned.
            return;
        }

        try
        {
            var inFlight = Interlocked.Increment(ref _refreshesInFlight);
            if (inFlight > PeakConcurrentRefreshes) PeakConcurrentRefreshes = inFlight;

            var state = await _reader.ReadAsync(_repoPath, ct);

            RepositoryName = new DirectoryInfo(state.RepoRoot).Name;
            BranchLabel = state.IsDetached
                ? "not on a branch (detached HEAD)"
                : state.Branch ?? "no branch";

            Changes.Update(state);
            History.Update(state);
            Branches.Update(state);
        }
        finally
        {
            Interlocked.Decrement(ref _refreshesInFlight);
            _refreshGate.Release();
        }
    }

    public void Dispose()
    {
        // Cancel first: this releases anything parked on the refresh gate. Note the gate
        // itself is deliberately NOT disposed — a holder mid-refresh would then throw
        // ObjectDisposedException on Release, and SemaphoreSlim only requires disposal when
        // its AvailableWaitHandle has been used, which this class never touches.
        _disposing.Cancel();

        Startup.RepositoryOpenedAsync = null;
        Explain.ActionCompletedAsync = null;
        _watcher.Dispose();
        CommandLog.Dispose();
        _disposing.Dispose();
    }

    private async Task OpenRepositoryAsync(string repoRoot, CancellationToken ct = default)
    {
        _repoPath = repoRoot;
        Explain.Clear();
        StatusMessage = null;

        await RefreshAsync(ct);

        IsRepositoryOpen = true;

        // The watcher fires on a thread-pool thread, so the refresh it triggers has to hop
        // to the UI thread before rebuilding collections bound to controls.
        _watcher.Watch(repoRoot);
    }

    private async Task OnActionCompletedAsync(ActionOutcome outcome, CancellationToken ct)
    {
        if (outcome.Narration is { Length: > 0 }) StatusMessage = outcome.Narration;

        Changes.OnActionCompleted(outcome);
        Branches.OnActionCompleted(outcome);

        await RefreshAsync(ct);
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
