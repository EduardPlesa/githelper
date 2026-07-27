using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHelper.Core.Actions;
using GitHelper.Core.Model;

namespace GitHelper.App.ViewModels;

/// <summary>
/// The Changes tab: what is staged, what is not, and the commit box. Every button routes
/// through the explain panel, which owns previewing, gating, and running.
/// </summary>
public sealed partial class ChangesViewModel : ViewModelBase
{
    private readonly ExplainPanelViewModel _explain;
    private string? _repoPath;

    public ChangesViewModel(ExplainPanelViewModel explain)
    {
        _explain = explain;

        StageAllCommand = new AsyncRelayCommand(() => InvokeAsync("stage-all", path: null));
        UnstageAllCommand = new AsyncRelayCommand(() => InvokeAsync("unstage-all", path: null));
        CommitCommand = new AsyncRelayCommand(CommitAsync);
        PushCommand = new AsyncRelayCommand(() => InvokeAsync("push", path: null));
    }

    public ObservableCollection<FileChangeRowViewModel> Staged { get; } = new();

    public ObservableCollection<FileChangeRowViewModel> Unstaged { get; } = new();

    [ObservableProperty] private string _commitMessage = string.Empty;
    [ObservableProperty] private bool _hasStagedChanges;
    [ObservableProperty] private bool _hasAnyChanges;
    [ObservableProperty] private bool _hasUnpushedCommits;
    [ObservableProperty] private string _unpushedSummary = string.Empty;

    public IAsyncRelayCommand StageAllCommand { get; }

    public IAsyncRelayCommand UnstageAllCommand { get; }

    public IAsyncRelayCommand CommitCommand { get; }

    /// <summary>
    /// The same "push" action the Branches tab offers, surfaced here as well. Committing
    /// happens on this tab, so ending the flow with no hint that the work is still local
    /// left the next step discoverable only by knowing where to look for it.
    /// </summary>
    public IAsyncRelayCommand PushCommand { get; }

    public void Update(RepoState state)
    {
        _repoPath = state.RepoRoot;

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
    }

    /// <summary>
    /// Decides whether to offer "send changes" here, and what to call the situation.
    /// Presentation only, and deliberately separate from BranchesViewModel's sync summary:
    /// that one describes the branch in both directions, this one answers a single question —
    /// is there local work that has not left this computer?
    /// </summary>
    private void UpdatePushPrompt(RepoState state)
    {
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
}
