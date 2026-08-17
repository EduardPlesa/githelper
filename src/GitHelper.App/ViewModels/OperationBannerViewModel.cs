using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHelper.Core.Actions;
using GitHelper.Core.Model;

namespace GitHelper.App.ViewModels;

/// <summary>
/// The band across the top of the window while git has an operation half-done.
///
/// It lives at shell level rather than in a tab because a paused merge is a property of the
/// repository, not of one view — and it renders from <see cref="RepoState.Operation"/>
/// rather than from an action outcome, so a merge left running when the app was last closed
/// shows up exactly like one started a moment ago.
///
/// Presentation only: both buttons go through the explain panel, so the ordinary
/// explain-confirm-narrate flow applies to them like any other action.
/// </summary>
public sealed partial class OperationBannerViewModel : ViewModelBase
{
    private readonly ExplainPanelViewModel _explain;
    private string? _repoPath;
    private OperationKind _kind = OperationKind.Merge;

    public OperationBannerViewModel(ExplainPanelViewModel explain)
    {
        _explain = explain;

        // The action ids depend on what is in flight, so they are read at click time from
        // _kind rather than baked into the command.
        FinishCommand = new AsyncRelayCommand(
            () => InvokeAsync(IsRebase ? "rebase-continue" : "merge-continue"), () => CanFinish);
        AbandonCommand = new AsyncRelayCommand(
            () => InvokeAsync(IsRebase ? "rebase-abort" : "merge-abort"));
        SkipCommand = new AsyncRelayCommand(() => InvokeAsync("rebase-skip"), () => CanSkip);
    }

    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private string _headline = string.Empty;
    [ObservableProperty] private string _detail = string.Empty;

    /// <summary>
    /// False while any file still conflicts. The precondition refuses in that case anyway;
    /// disabling the button is friendlier than letting the click land on a refusal.
    /// </summary>
    [ObservableProperty] private bool _canFinish;

    /// <summary>Only a rebase can skip; a merge has no per-commit sequence to skip within.</summary>
    [ObservableProperty] private bool _canSkip;

    [ObservableProperty] private string _finishLabel = "Finish the merge";
    [ObservableProperty] private string _abandonLabel = "Abandon the merge";

    public IAsyncRelayCommand FinishCommand { get; }

    public IAsyncRelayCommand AbandonCommand { get; }

    /// <summary>
    /// Drops the commit the rebase stopped on. Destructive, so it goes through the modal like
    /// any other — the band is not a shortcut past the gate.
    /// </summary>
    public IAsyncRelayCommand SkipCommand { get; }

    private bool IsRebase => _kind == OperationKind.Rebase;

    public void Update(RepoState state)
    {
        _repoPath = state.RepoRoot;

        if (state.Operation is null)
        {
            IsVisible = false;
            Headline = string.Empty;
            Detail = string.Empty;
            CanFinish = false;
            CanSkip = false;
            return;
        }

        _kind = state.Operation.Kind;

        var remaining = state.Unmerged.Count;
        var branch = state.Branch ?? "this branch";

        IsVisible = true;
        CanFinish = remaining == 0;
        CanSkip = IsRebase;
        FinishLabel = IsRebase ? "Continue" : "Finish the merge";
        AbandonLabel = IsRebase ? "Abandon the update" : "Abandon the merge";
        Headline = IsRebase
            ? RebaseHeadline(state, branch)
            : $"You are part-way through merging "
              + $"{state.Operation.IncomingLabel ?? "another branch"} into {branch}.";
        Detail = DescribeWhatIsLeft(state, remaining);
    }

    private static string RebaseHeadline(RepoState state, string branch)
    {
        var onto = state.Operation!.IncomingLabel ?? "another branch";
        var progress = state.Operation.Rebase;

        // No counter when git did not say where it is, rather than a made-up one.
        return progress is null
            ? $"You are part-way through updating {branch} onto {onto}."
            : $"You are part-way through updating {branch} onto {onto} — "
              + $"commit {progress.Step} of {progress.Total}.";
    }

    private string DescribeWhatIsLeft(RepoState state, int remaining)
    {
        if (remaining > 0)
        {
            var stoppedAt = state.Operation!.Rebase?.StoppedAtSubject;
            var prefix = stoppedAt is null
                ? string.Empty
                : $"Stopped on your commit \"{stoppedAt}\". ";

            return prefix
                   + $"{remaining} file(s) have changes git could not combine. Open each one, "
                   + "fix the marked sections, then mark it fixed in the Changes tab.";
        }

        return IsRebase
            ? "All conflicts fixed. Carry on to replay the rest of your commits."
            : "All conflicts fixed. Finish the merge to save it as a commit.";
    }

    partial void OnCanFinishChanged(bool value) => FinishCommand.NotifyCanExecuteChanged();

    partial void OnCanSkipChanged(bool value) => SkipCommand.NotifyCanExecuteChanged();

    private Task InvokeAsync(string actionId)
        => _repoPath is null
            ? Task.CompletedTask
            : _explain.ShowAndRunIfUngatedAsync(_repoPath, new ActionRequest(actionId));
}
