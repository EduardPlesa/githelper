using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHelper.Core.Actions;
using GitHelper.Core.Model;

namespace GitHelper.App.ViewModels;

/// <summary>
/// The Branches tab: which branch you are on, what else exists, and the sync actions.
/// </summary>
public sealed partial class BranchesViewModel : ViewModelBase
{
    private readonly ExplainPanelViewModel _explain;
    private string? _repoPath;

    public BranchesViewModel(ExplainPanelViewModel explain)
    {
        _explain = explain;

        CreateBranchCommand = new AsyncRelayCommand(
            () => InvokeAsync("create-branch", branchName: NewBranchName));
        FetchCommand = new AsyncRelayCommand(() => InvokeAsync("fetch"));
        PullCommand = new AsyncRelayCommand(() => InvokeAsync("pull"));
    }

    public ObservableCollection<BranchRowViewModel> Branches { get; } = new();

    [ObservableProperty] private string _newBranchName = string.Empty;
    [ObservableProperty] private string _currentBranchLabel = string.Empty;
    [ObservableProperty] private bool _isDetached;
    [ObservableProperty] private bool _hasRemote;
    [ObservableProperty] private string _syncSummary = string.Empty;

    public IAsyncRelayCommand CreateBranchCommand { get; }

    public IAsyncRelayCommand FetchCommand { get; }

    /// <summary>
    /// Fetch and pull bring work in, which is a branch-level concern. Sending work out lives
    /// on the Changes tab instead, beside the Commit button — see ChangesViewModel.PushCommand.
    /// </summary>
    public IAsyncRelayCommand PullCommand { get; }

    public void Update(RepoState state)
    {
        _repoPath = state.RepoRoot;

        Branches.Clear();
        foreach (var branch in state.Branches)
        {
            var isCurrent = !state.IsDetached
                && string.Equals(branch.Name, state.Branch, StringComparison.Ordinal);
            Branches.Add(new BranchRowViewModel(branch, isCurrent, InvokeWithBranchAsync));
        }

        IsDetached = state.IsDetached;
        HasRemote = state.HasRemote;
        CurrentBranchLabel = state.IsDetached
            // Told plainly rather than shown as a blank branch name.
            ? "You are not on a branch (git calls this a detached HEAD)"
            : state.Branch ?? "no branch";
        SyncSummary = DescribeSync(state);
    }

    /// <summary>
    /// Clears the name box only when a branch with that name observably appeared, mirroring
    /// how ChangesViewModel treats the commit box. Reacting to bare success instead would
    /// discard the typed name when the user clicks Fetch or Pull before Create.
    /// </summary>
    public void OnActionCompleted(ActionOutcome outcome)
    {
        if (!outcome.Success || string.IsNullOrEmpty(NewBranchName)) return;

        var existedBefore = outcome.Before.Branches.Any(
            b => string.Equals(b.Name, NewBranchName, StringComparison.Ordinal));
        var existsAfter = outcome.After.Branches.Any(
            b => string.Equals(b.Name, NewBranchName, StringComparison.Ordinal));

        if (!existedBefore && existsAfter) NewBranchName = string.Empty;
    }

    /// <summary>
    /// Presentation only. Deliberately not the engine's Narrator, which describes a
    /// transition between two states rather than the current one.
    /// </summary>
    private static string DescribeSync(RepoState state)
    {
        if (state.Upstream is null)
            return "This branch is not linked to the server yet";

        return (state.Ahead, state.Behind) switch
        {
            (0, 0) => $"in step with {state.Upstream}",
            (1, 0) => "1 commit to send",
            ( > 1, 0) => $"{state.Ahead} commits to send",
            (0, 1) => "1 commit to get",
            (0, > 1) => $"{state.Behind} commits to get",
            var (ahead, behind) => $"{ahead} to send, {behind} to get",
        };
    }

    private Task InvokeWithBranchAsync(string actionId, string branchName)
        => InvokeAsync(actionId, branchName);

    private Task InvokeAsync(string actionId, string? branchName = null)
        => _repoPath is null
            ? Task.CompletedTask
            : _explain.ShowAndRunIfUngatedAsync(
                _repoPath, new ActionRequest(actionId, BranchName: branchName));
}
