using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GitHelper.Core.Actions;
using GitHelper.Core.Model;

namespace GitHelper.App.ViewModels;

/// <summary>The History tab: recent commits, newest first, with undo on the newest only.</summary>
public sealed partial class HistoryViewModel(ExplainPanelViewModel explain) : ViewModelBase
{
    private string? _repoPath;

    public ObservableCollection<CommitRowViewModel> Commits { get; } = new();

    [ObservableProperty] private bool _hasCommits;

    /// <summary>Overridable so relative-date rendering is deterministic in tests.</summary>
    public Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.Now;

    public void Update(RepoState state)
    {
        _repoPath = state.RepoRoot;
        var now = Now();

        Commits.Clear();
        for (var i = 0; i < state.RecentCommits.Count; i++)
        {
            // Undo applies to the newest commit only, and only when the engine says the
            // repository has a parent commit to step back to.
            var canUndo = i == 0 && state.CanUndoLastCommit;
            Commits.Add(new CommitRowViewModel(state.RecentCommits[i], canUndo, UndoAsync, now));
        }

        HasCommits = Commits.Count > 0;
    }

    private Task UndoAsync()
        => _repoPath is null
            ? Task.CompletedTask
            // undo-last-commit is Caution, so this previews and waits for confirmation.
            : explain.ShowAndRunIfUngatedAsync(_repoPath, new ActionRequest("undo-last-commit"));
}
