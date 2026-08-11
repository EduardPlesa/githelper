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

    public OperationBannerViewModel(ExplainPanelViewModel explain)
    {
        _explain = explain;

        FinishCommand = new AsyncRelayCommand(() => InvokeAsync("merge-continue"), () => CanFinish);
        AbandonCommand = new AsyncRelayCommand(() => InvokeAsync("merge-abort"));
    }

    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private string _headline = string.Empty;
    [ObservableProperty] private string _detail = string.Empty;

    /// <summary>
    /// False while any file still conflicts. The precondition refuses in that case anyway;
    /// disabling the button is friendlier than letting the click land on a refusal.
    /// </summary>
    [ObservableProperty] private bool _canFinish;

    public IAsyncRelayCommand FinishCommand { get; }

    public IAsyncRelayCommand AbandonCommand { get; }

    public void Update(RepoState state)
    {
        _repoPath = state.RepoRoot;

        if (state.Operation is null)
        {
            IsVisible = false;
            Headline = string.Empty;
            Detail = string.Empty;
            CanFinish = false;
            return;
        }

        var incoming = state.Operation.IncomingLabel ?? "another branch";
        var remaining = state.Unmerged.Count;

        IsVisible = true;
        CanFinish = remaining == 0;
        Headline = $"You are part-way through merging {incoming} into {state.Branch ?? "this branch"}.";
        Detail = remaining == 0
            ? "All conflicts fixed. Finish the merge to save it as a commit."
            : $"{remaining} file(s) have changes git could not combine. Open each one, fix the "
              + "marked sections, then mark it fixed in the Changes tab.";
    }

    partial void OnCanFinishChanged(bool value) => FinishCommand.NotifyCanExecuteChanged();

    private Task InvokeAsync(string actionId)
        => _repoPath is null
            ? Task.CompletedTask
            : _explain.ShowAndRunIfUngatedAsync(_repoPath, new ActionRequest(actionId));
}
