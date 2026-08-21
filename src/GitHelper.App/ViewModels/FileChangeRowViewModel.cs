using CommunityToolkit.Mvvm.Input;
using GitHelper.Core.Model;
using GitHelper.Core.Repo;

namespace GitHelper.App.ViewModels;

/// <summary>
/// One file row in the Changes view. The same <see cref="FileChange"/> can back two rows —
/// one staged, one not — when a file was staged and then edited again.
/// </summary>
public sealed class FileChangeRowViewModel : ViewModelBase
{
    public FileChangeRowViewModel(
        FileChange change,
        bool staged,
        Func<string, string, Task> invokeAction,
        Func<string, DiffSide, string?, Task>? viewChanges = null)
    {
        Path = change.Path;
        IsStaged = staged;
        IsUntracked = change.IsUntracked;
        IsUnmerged = change.IsUnmerged;
        StatusLabel = DescribeKind(staged ? change.IndexChange : change.WorkTreeChange);

        StageCommand = new AsyncRelayCommand(() => invokeAction("stage-file", change.Path));
        UnstageCommand = new AsyncRelayCommand(() => invokeAction("unstage-file", change.Path));
        DiscardCommand = new AsyncRelayCommand(() => invokeAction("discard-file", change.Path));
        MarkResolvedCommand = new AsyncRelayCommand(() => invokeAction("mark-resolved", change.Path));

        // Two rows have no diff to show, and both are hidden rather than left to fail.
        //
        // A conflicted file's diff is git's combined format, a grammar this app does not read
        // yet. That surface belongs to guided conflict resolution, not here.
        //
        // A path ending in '/' is a wholly untracked folder: git status runs with the default
        // -u normal, which collapses one into a single entry. A folder has no single diff —
        // `diff --no-index -- /dev/null newdir/` fails outright — and offering a button whose
        // only outcome is "the changes in this file could not be read" is worse than offering
        // none, especially since making a folder of new files is beginner's work.
        CanViewChanges = viewChanges is not null
            && !change.IsUnmerged
            && !change.Path.EndsWith('/');

        var side = staged
            ? DiffSide.Staged
            : change.IsUntracked ? DiffSide.Untracked : DiffSide.Unstaged;

        ViewChangesCommand = new AsyncRelayCommand(
            () => viewChanges is null
                ? Task.CompletedTask
                : viewChanges(change.Path, side, change.OriginalPath),
            () => CanViewChanges);
    }

    public string Path { get; }

    /// <summary>Plain English, never git's status letters.</summary>
    public string StatusLabel { get; }

    public bool IsStaged { get; }

    public bool IsUntracked { get; }

    /// <summary>
    /// A file git could not combine on its own. Such a row offers only
    /// <see cref="MarkResolvedCommand"/>: staging or discarding it mid-merge would be
    /// answering a question the user has not been asked yet.
    /// </summary>
    public bool IsUnmerged { get; }

    public IAsyncRelayCommand StageCommand { get; }

    public IAsyncRelayCommand UnstageCommand { get; }

    public IAsyncRelayCommand DiscardCommand { get; }

    public IAsyncRelayCommand MarkResolvedCommand { get; }

    /// <summary>False for a conflicted file, which has no diff this app can read yet.</summary>
    public bool CanViewChanges { get; }

    public IAsyncRelayCommand ViewChangesCommand { get; }

    private static string DescribeKind(ChangeKind kind) => kind switch
    {
        ChangeKind.Added => "new file",
        ChangeKind.Untracked => "new file",
        ChangeKind.Modified => "modified",
        ChangeKind.Deleted => "deleted",
        ChangeKind.Renamed => "renamed",
        ChangeKind.Copied => "copied",
        ChangeKind.Unmerged => "conflicted",
        _ => "changed",
    };
}
