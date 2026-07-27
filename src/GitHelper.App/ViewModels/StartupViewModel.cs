using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHelper.App.Infrastructure;
using GitHelper.App.Settings;
using GitHelper.Core.Git;
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

    public StartupViewModel(
        ISettingsStore settings,
        IFolderPicker picker,
        RepoStateReader reader,
        GitEnvironment environment)
    {
        _settings = settings;
        _picker = picker;
        _reader = reader;
        _environment = environment;

        BrowseCommand = new AsyncRelayCommand(BrowseAsync);
    }

    public ObservableCollection<RecentRepoViewModel> Recents { get; } = new();

    [ObservableProperty] private StartupState _state = StartupState.Checking;
    [ObservableProperty] private string? _blockingMessage;
    [ObservableProperty] private string? _blockingFixHint;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _identityPromptNeeded;

    public IAsyncRelayCommand BrowseCommand { get; }

    /// <summary>
    /// Invoked with the repository root once a folder validates, and awaited — the overlay
    /// must not be considered finished until the shell has actually loaded the repository.
    /// A plain event could not be awaited, which previously forced the subscriber to block.
    /// </summary>
    public Func<string, CancellationToken, Task>? RepositoryOpenedAsync { get; set; }

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
            ErrorMessage =
                "That folder is not a git project. Git keeps its history in a hidden .git "
                + "folder, and there is not one here or in any folder above it.";
            return;
        }

        // Record the resolved root, so a recents entry reopens the project rather than a
        // subfolder the user happened to pick.
        _settings.Save(_settings.Load().WithRepositoryOpened(root));
        LoadRecents();

        if (RepositoryOpenedAsync is { } handler) await handler(root, ct);
    }

    private async Task BrowseAsync()
    {
        var chosen = await _picker.PickFolderAsync("Choose a folder containing a git project");
        if (chosen is null) return; // cancelled

        await OpenAsync(chosen);
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
    }
}
