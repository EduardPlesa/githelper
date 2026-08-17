using CommunityToolkit.Mvvm.Input;
using GitHelper.Core.Model;

namespace GitHelper.App.ViewModels;

/// <summary>One branch in the Branches view.</summary>
public sealed class BranchRowViewModel : ViewModelBase
{
    public BranchRowViewModel(
        BranchInfo branch,
        bool isCurrent,
        Func<string, string, Task> invokeAction)
    {
        Name = branch.Name;
        IsCurrent = isCurrent;
        UpstreamLabel = branch.Upstream ?? "not on the server yet";

        // You cannot switch to the branch you are already on, and git refuses to delete it.
        // Disabling the buttons is friendlier than letting the click land on a refusal.
        CanSwitch = !isCurrent;
        CanDelete = !isCurrent;

        // Nor can you merge a branch into itself, or replay your commits onto themselves.
        CanMerge = !isCurrent;
        CanRebase = !isCurrent;

        SwitchCommand = new AsyncRelayCommand(
            () => invokeAction("switch-branch", branch.Name), () => CanSwitch);
        DeleteCommand = new AsyncRelayCommand(
            () => invokeAction("delete-branch", branch.Name), () => CanDelete);
        MergeCommand = new AsyncRelayCommand(
            () => invokeAction("merge", branch.Name), () => CanMerge);
        RebaseCommand = new AsyncRelayCommand(
            () => invokeAction("rebase", branch.Name), () => CanRebase);
    }

    public string Name { get; }

    public string UpstreamLabel { get; }

    public bool IsCurrent { get; }

    public bool CanSwitch { get; }

    public bool CanDelete { get; }

    public bool CanMerge { get; }

    /// <summary>
    /// Deliberately not false when the current branch is already on a server. The rebase
    /// action refuses that with an explanation, and a refusal the user can read beats a
    /// disabled button they cannot ask a question of.
    /// </summary>
    public bool CanRebase { get; }

    public IAsyncRelayCommand SwitchCommand { get; }

    public IAsyncRelayCommand DeleteCommand { get; }

    /// <summary>
    /// Brings this branch's work into the one you are on. Caution, so it previews and waits
    /// rather than running on click — it can stop half-way on conflicts.
    /// </summary>
    public IAsyncRelayCommand MergeCommand { get; }

    /// <summary>
    /// Replays the commits on the branch you are on so they start from this one. Caution for
    /// the same reason, and refused outright once your branch is on a server.
    /// </summary>
    public IAsyncRelayCommand RebaseCommand { get; }
}
