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
    }

    public ObservableCollection<FileChangeRowViewModel> Staged { get; } = new();

    public ObservableCollection<FileChangeRowViewModel> Unstaged { get; } = new();

    [ObservableProperty] private string _commitMessage = string.Empty;
    [ObservableProperty] private bool _hasStagedChanges;
    [ObservableProperty] private bool _hasAnyChanges;

    public IAsyncRelayCommand StageAllCommand { get; }

    public IAsyncRelayCommand UnstageAllCommand { get; }

    public IAsyncRelayCommand CommitCommand { get; }

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
