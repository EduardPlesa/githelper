using CommunityToolkit.Mvvm.Input;
using GitHelper.Core.Model;

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
        Func<string, string, Task> invokeAction)
    {
        Path = change.Path;
        IsStaged = staged;
        IsUntracked = change.IsUntracked;
        StatusLabel = DescribeKind(staged ? change.IndexChange : change.WorkTreeChange);

        StageCommand = new AsyncRelayCommand(() => invokeAction("stage-file", change.Path));
        UnstageCommand = new AsyncRelayCommand(() => invokeAction("unstage-file", change.Path));
        DiscardCommand = new AsyncRelayCommand(() => invokeAction("discard-file", change.Path));
    }

    public string Path { get; }

    /// <summary>Plain English, never git's status letters.</summary>
    public string StatusLabel { get; }

    public bool IsStaged { get; }

    public bool IsUntracked { get; }

    public IAsyncRelayCommand StageCommand { get; }

    public IAsyncRelayCommand UnstageCommand { get; }

    public IAsyncRelayCommand DiscardCommand { get; }

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
