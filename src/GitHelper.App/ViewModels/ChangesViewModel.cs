using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHelper.App.Infrastructure;
using GitHelper.Core.Actions;
using GitHelper.Core.Model;
using GitHelper.Core.Setup;

namespace GitHelper.App.ViewModels;

/// <summary>
/// The Changes tab: what is staged, what is not, and the commit box. Every button routes
/// through the explain panel, which owns previewing, gating, and running.
/// </summary>
public sealed partial class ChangesViewModel : ViewModelBase
{
    /// <summary>
    /// GitHub's create-a-repository page. A constant, never anything the user typed: the
    /// only address this app ever opens is this one.
    /// </summary>
    public const string NewRepositoryUrl = "https://github.com/new";

    private readonly ExplainPanelViewModel _explain;
    private readonly IBrowserLauncher? _browser;
    private string? _repoPath;
    private FolderState? _folder;

    public ChangesViewModel(ExplainPanelViewModel explain, IBrowserLauncher? browser = null)
    {
        _explain = explain;
        _browser = browser;

        StageAllCommand = new AsyncRelayCommand(() => InvokeAsync("stage-all", path: null));
        UnstageAllCommand = new AsyncRelayCommand(() => InvokeAsync("unstage-all", path: null));
        CommitCommand = new AsyncRelayCommand(CommitAsync);
        PushCommand = new AsyncRelayCommand(() => InvokeAsync("push", path: null));
        CreateGitignoreCommand = new AsyncRelayCommand(CreateGitignoreAsync);
        ConnectRemoteCommand = new AsyncRelayCommand(ConnectRemoteAsync);
        OpenGitHubCommand = new RelayCommand(() => _browser?.Open(NewRepositoryUrl));
    }

    public ObservableCollection<FileChangeRowViewModel> Staged { get; } = new();

    public ObservableCollection<FileChangeRowViewModel> Unstaged { get; } = new();

    [ObservableProperty] private string _commitMessage = string.Empty;
    [ObservableProperty] private bool _hasStagedChanges;
    [ObservableProperty] private bool _hasAnyChanges;
    [ObservableProperty] private bool _hasUnpushedCommits;
    [ObservableProperty] private string _unpushedSummary = string.Empty;
    [ObservableProperty] private bool _hasGitignoreOffer;
    [ObservableProperty] private bool _hasNoRemoteOffer;
    [ObservableProperty] private string _remoteUrl = string.Empty;

    public IAsyncRelayCommand StageAllCommand { get; }

    public IAsyncRelayCommand UnstageAllCommand { get; }

    public IAsyncRelayCommand CommitCommand { get; }

    /// <summary>
    /// The same "push" action the Branches tab offers, surfaced here as well. Committing
    /// happens on this tab, so ending the flow with no hint that the work is still local
    /// left the next step discoverable only by knowing where to look for it.
    /// </summary>
    public IAsyncRelayCommand PushCommand { get; }

    public IAsyncRelayCommand CreateGitignoreCommand { get; }

    /// <summary>
    /// Previews `git remote add origin <url>`. Caution, so it waits for the panel's inline
    /// Confirm rather than running on click.
    /// </summary>
    public IAsyncRelayCommand ConnectRemoteCommand { get; }

    /// <summary>
    /// Opens github.com/new so the user can create the empty repository themselves. The app
    /// stops here on purpose: creating it for them would need a token, and this app has no
    /// field for one.
    /// </summary>
    public IRelayCommand OpenGitHubCommand { get; }

    public void Update(RepoState state, FolderState? folder)
    {
        _repoPath = state.RepoRoot;
        _folder = folder;

        Staged.Clear();
        foreach (var change in state.Staged)
            Staged.Add(new FileChangeRowViewModel(change, staged: true, InvokeWithPathAsync));

        Unstaged.Clear();
        // RepoState.Unstaged excludes untracked files by design; the view shows one
        // combined "not staged" list.
        foreach (var change in state.Unstaged.Concat(state.Untracked))
            Unstaged.Add(new FileChangeRowViewModel(change, staged: false, InvokeWithPathAsync));

        HasStagedChanges = Staged.Count > 0;
        HasAnyChanges = Staged.Count > 0 || Unstaged.Count > 0;

        UpdatePushPrompt(state);

        // Offered only when the folder is known and has none. A repository with a .gitignore
        // already curated by the user is none of the app's business.
        HasGitignoreOffer = folder is { HasGitignore: false };
    }

    /// <summary>
    /// Decides whether to offer "send changes" here, and what to call the situation.
    /// Presentation only, and deliberately separate from BranchesViewModel's sync summary:
    /// that one describes the branch in both directions, this one answers a single question —
    /// is there local work that has not left this computer?
    /// </summary>
    private void UpdatePushPrompt(RepoState state)
    {
        // The third state, and the one a new project starts in: there is no online copy at
        // all. Mutually exclusive with the send prompt below, which suppresses itself
        // whenever HasRemote is false.
        HasNoRemoteOffer = !state.HasRemote;

        // Every precondition on the push action must already hold. Offering the button
        // otherwise would show a beginner a control whose only possible outcome is a
        // blocked-action message.
        if (!state.HasRemote || !state.HasCommits || state.IsDetached)
        {
            HasUnpushedCommits = false;
            UnpushedSummary = string.Empty;
            return;
        }

        if (state.Upstream is null)
        {
            // With no upstream there is no ahead count to report — but this is exactly the
            // case that matters most, because none of this branch is on the server at all.
            HasUnpushedCommits = true;
            UnpushedSummary = "This branch is not on the server yet";
            return;
        }

        HasUnpushedCommits = state.Ahead > 0;
        UnpushedSummary = state.Ahead switch
        {
            <= 0 => string.Empty,
            1 => "1 commit to send",
            _ => $"{state.Ahead} commits to send",
        };
    }

    /// <summary>
    /// Clears the commit box only when a commit observably appeared. Driven by the
    /// before/after snapshots rather than by what was requested, so a failed commit never
    /// loses the message the user typed.
    /// </summary>
    public void OnActionCompleted(ActionOutcome outcome)
    {
        if (outcome.Success
            && outcome.After.RecentCommits.Count > outcome.Before.RecentCommits.Count)
        {
            CommitMessage = string.Empty;
        }

        // Driven by a remote observably appearing, not by which action was requested, so a
        // rejected address stays in the box for the user to correct.
        if (outcome.Success && !outcome.Before.HasRemote && outcome.After.HasRemote)
            RemoteUrl = string.Empty;
    }

    private Task InvokeWithPathAsync(string actionId, string path) => InvokeAsync(actionId, path);

    /// <summary>
    /// Previews, then runs immediately unless the action needs an inline Confirm. Every
    /// row/bulk action here (stage, unstage, discard) is a one-click user action: Safe ones
    /// just execute, and the sole Destructive one (discard-file) is gated by the native modal
    /// inside <see cref="ExplainPanelViewModel.RunAsync"/> rather than an inline Confirm, so
    /// <see cref="ExplainPanelViewModel.ShouldRunImmediately"/> still lets it through here and
    /// the modal does the gating. Caution actions are held for an inline Confirm instead.
    /// </summary>
    private async Task InvokeAsync(string actionId, string? path)
    {
        if (_repoPath is null) return;
        await _explain.ShowAndRunIfUngatedAsync(_repoPath, new ActionRequest(actionId, Path: path));
    }

    private Task CommitAsync()
        => _repoPath is null
            ? Task.CompletedTask
            // Commit is Caution, so this only previews; the user confirms from the panel.
            : _explain.ShowAndRunIfUngatedAsync(
                _repoPath, new ActionRequest("commit", Message: CommitMessage));

    private Task ConnectRemoteAsync()
        => _repoPath is null
            ? Task.CompletedTask
            : _explain.ShowAndRunIfUngatedAsync(
                _repoPath, new ActionRequest("connect-remote", RemoteUrl: RemoteUrl));

    private Task CreateGitignoreAsync()
        => _folder is null
            ? Task.CompletedTask
            // Previews only. The user confirms from the panel, like every other operation.
            : _explain.ShowSetupAsync(_folder.Path, new SetupRequest(SetupService.CreateGitignore));
}
