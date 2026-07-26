using CommunityToolkit.Mvvm.Input;
using GitHelper.App.Infrastructure;
using GitHelper.Core.Model;

namespace GitHelper.App.ViewModels;

/// <summary>One commit in the History view.</summary>
public sealed class CommitRowViewModel : ViewModelBase
{
    public CommitRowViewModel(CommitInfo commit, bool canUndo, Func<Task> undo, DateTimeOffset now)
    {
        ShortHash = commit.ShortHash;
        Subject = commit.Subject;
        Author = commit.Author;
        RelativeDate = RelativeTime.Describe(commit.Date, now);
        CanUndo = canUndo;
        UndoCommand = new AsyncRelayCommand(undo, () => CanUndo);
    }

    public string ShortHash { get; }

    public string Subject { get; }

    public string Author { get; }

    public string RelativeDate { get; }

    /// <summary>True only on the newest commit, and only when the engine allows undoing it.</summary>
    public bool CanUndo { get; }

    public IAsyncRelayCommand UndoCommand { get; }
}
