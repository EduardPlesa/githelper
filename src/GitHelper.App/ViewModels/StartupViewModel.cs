using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHelper.App.Infrastructure;
using GitHelper.App.Settings;
using GitHelper.Core.Git;
using GitHelper.Core.Model;
using GitHelper.Core.Repo;

namespace GitHelper.App.ViewModels;

public enum StartupState
{
    /// <summary>Running the environment check.</summary>
    Checking,

    /// <summary>Showing the recents list and the Browse button.</summary>
    AwaitingChoice,

    /// <summary>Git is not installed — nothing else in the app can work.</summary>
    GitMissing,

    /// <summary>A folder was chosen that is not a repository yet; offering to create one.</summary>
    FolderIsNotARepository,
}

/// <summary>
/// The overlay shown over the empty window until a repository is open. Asks every launch
/// rather than silently reopening, with a recents list so that ask is one click.
/// </summary>
public sealed partial class StartupViewModel : ViewModelBase
{
    private readonly ISettingsStore _settings;
    private readonly IFolderPicker _picker;
    private readonly RepoStateReader _reader;
    private readonly GitEnvironment _environment;
    private readonly FolderInspector _inspector;

    public StartupViewModel(
        ISettingsStore settings,
        IFolderPicker picker,
        RepoStateReader reader,
        GitEnvironment environment,
        FolderInspector inspector)
    {
        _settings = settings;
        _picker = picker;
        _reader = reader;
        _environment = environment;
        _inspector = inspector;

        BrowseCommand = new AsyncRelayCommand(BrowseAsync);
        SaveIdentityCommand = new AsyncRelayCommand(SaveIdentityAsync, () => CanSaveIdentity);
        StartTrackingCommand = new AsyncRelayCommand(StartTrackingAsync, () => PendingFolder is not null);
    }

    public ObservableCollection<RecentRepoViewModel> Recents { get; } = new();

    [ObservableProperty] private StartupState _state = StartupState.Checking;
    [ObservableProperty] private string? _blockingMessage;
    [ObservableProperty] private string? _blockingFixHint;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _identityPromptNeeded;
    [ObservableProperty] private string _identityName = string.Empty;
    [ObservableProperty] private string _identityEmail = string.Empty;
    [ObservableProperty] private string? _identitySaveError;
    [ObservableProperty] private FolderState? _pendingFolder;

    public IAsyncRelayCommand BrowseCommand { get; }

    public IAsyncRelayCommand SaveIdentityCommand { get; }

    public IAsyncRelayCommand StartTrackingCommand { get; }

    public bool CanSaveIdentity =>
        !string.IsNullOrWhiteSpace(IdentityName) && !string.IsNullOrWhiteSpace(IdentityEmail);

    public bool HasIdentitySaveError => !string.IsNullOrEmpty(IdentitySaveError);

    // Compiled bindings cannot compare an enum to a constant, so the overlay's conditions
    // are exposed as booleans instead of pushed into XAML converters.
    public bool IsChecking => State == StartupState.Checking;

    public bool IsAwaitingChoice => State == StartupState.AwaitingChoice;

    public bool IsGitMissing => State == StartupState.GitMissing;

    public bool IsOfferingInit => State == StartupState.FolderIsNotARepository;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool HasRecents => Recents.Count > 0;

    /// <summary>
    /// Invoked with the repository root once a folder validates, and awaited — the overlay
    /// must not be considered finished until the shell has actually loaded the repository.
    /// A plain event could not be awaited, which previously forced the subscriber to block.
    /// </summary>
    public Func<string, CancellationToken, Task>? RepositoryOpenedAsync { get; set; }

    /// <summary>Raised when the user accepts the offer. The shell routes it to the explain panel.</summary>
    public Func<string, CancellationToken, Task>? InitRequestedAsync { get; set; }

    /// <summary>
    /// An empty folder and a folder full of work are the same command but different
    /// situations, and a beginner needs to be told which one they are in.
    /// </summary>
    public string PendingFolderSummary => PendingFolder switch
    {
        null => string.Empty,
        { FileCount: 0 } => "This folder is empty. That is fine — you can start tracking now "
                            + "and add files later.",
        { FileCount: 1 } => "I found 1 file here. Tracking lets you save versions of it.",
        var folder => $"I found {folder.FileCount} files here. "
                      + "Tracking lets you save versions of them.",
    };

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var checks = await _environment.CheckAsync(ct);

        var blocking = checks.FirstOrDefault(c => c.Status == CheckStatus.Blocking);
        if (blocking is not null)
        {
            // No point offering to open a repository when git itself cannot run.
            BlockingMessage = blocking.Explanation;
            BlockingFixHint = blocking.FixHint;
            State = StartupState.GitMissing;
            return;
        }

        // A missing identity is a warning, not a blocker: browsing and staging still work,
        // and surfacing it now prevents a confusing failure on the first commit.
        IdentityPromptNeeded = checks.Any(
            c => c.Id == "git-identity" && c.Status == CheckStatus.Warning);

        LoadRecents();
        State = StartupState.AwaitingChoice;
    }

    public async Task OpenAsync(string path, CancellationToken ct = default)
    {
        ErrorMessage = null;

        var root = await _reader.FindRepoRootAsync(path, ct);
        if (root is null)
        {
            // Not an error any more: this is where a project starts. The folder is deliberately
            // not added to recents, because it is not a project yet.
            PendingFolder = _inspector.Inspect(path);
            State = StartupState.FolderIsNotARepository;
            return;
        }

        // Record the resolved root, so a recents entry reopens the project rather than a
        // subfolder the user happened to pick.
        _settings.Save(_settings.Load().WithRepositoryOpened(root));
        LoadRecents();

        if (RepositoryOpenedAsync is { } handler) await handler(root, ct);
    }

    private Task StartTrackingAsync()
        => PendingFolder is { } folder && InitRequestedAsync is { } handler
            ? handler(folder.Path, CancellationToken.None)
            : Task.CompletedTask;

    private async Task BrowseAsync()
    {
        var chosen = await _picker.PickFolderAsync("Choose a folder containing a git project");
        if (chosen is null) return; // cancelled

        await OpenAsync(chosen);
    }

    private async Task SaveIdentityAsync()
    {
        IdentitySaveError = null;

        var result = await _environment.SetIdentityAsync(
            IdentityName.Trim(), IdentityEmail.Trim());

        if (!result.Success)
        {
            // Never report success when git refused — the first commit would then fail
            // exactly as confusingly as if the prompt had never appeared.
            IdentitySaveError =
                "Git could not save that. " + (result.StdErr.Trim() is { Length: > 0 } detail
                    ? detail
                    : "You can set it yourself with: git config --global user.name \"Your Name\"");
            return;
        }

        IdentityPromptNeeded = false;
    }

    private void RemoveRecent(string path)
    {
        _settings.Save(_settings.Load().WithRepositoryRemoved(path));
        LoadRecents();
    }

    private void LoadRecents()
    {
        Recents.Clear();
        foreach (var path in _settings.Load().RecentRepositories)
            Recents.Add(new RecentRepoViewModel(path, p => OpenAsync(p), RemoveRecent));

        OnPropertyChanged(nameof(HasRecents));
    }

    partial void OnStateChanged(StartupState value)
    {
        OnPropertyChanged(nameof(IsChecking));
        OnPropertyChanged(nameof(IsAwaitingChoice));
        OnPropertyChanged(nameof(IsGitMissing));
        OnPropertyChanged(nameof(IsOfferingInit));
    }

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    partial void OnPendingFolderChanged(FolderState? value)
    {
        OnPropertyChanged(nameof(PendingFolderSummary));
        StartTrackingCommand.NotifyCanExecuteChanged();
    }

    partial void OnIdentityNameChanged(string value)
    {
        OnPropertyChanged(nameof(CanSaveIdentity));
        SaveIdentityCommand.NotifyCanExecuteChanged();
    }

    partial void OnIdentityEmailChanged(string value)
    {
        OnPropertyChanged(nameof(CanSaveIdentity));
        SaveIdentityCommand.NotifyCanExecuteChanged();
    }

    partial void OnIdentitySaveErrorChanged(string? value)
        => OnPropertyChanged(nameof(HasIdentitySaveError));
}
