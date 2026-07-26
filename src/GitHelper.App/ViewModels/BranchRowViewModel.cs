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

        SwitchCommand = new AsyncRelayCommand(
            () => invokeAction("switch-branch", branch.Name), () => CanSwitch);
        DeleteCommand = new AsyncRelayCommand(
            () => invokeAction("delete-branch", branch.Name), () => CanDelete);
    }

    public string Name { get; }

    public string UpstreamLabel { get; }

    public bool IsCurrent { get; }

    public bool CanSwitch { get; }

    public bool CanDelete { get; }

    public IAsyncRelayCommand SwitchCommand { get; }

    public IAsyncRelayCommand DeleteCommand { get; }
}
