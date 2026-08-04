using CommunityToolkit.Mvvm.Input;
using GitHelper.Core.Model;

namespace GitHelper.App.ViewModels;

/// <summary>One stash entry in the Changes view.</summary>
public sealed class StashRowViewModel : ViewModelBase
{
    public StashRowViewModel(StashInfo stash, Func<string, string, Task> invokeAction)
    {
        Message = stash.Message;
        RefLabel = stash.Ref;

        PopCommand = new AsyncRelayCommand(() => invokeAction("stash-pop", stash.Ref));
        ApplyCommand = new AsyncRelayCommand(() => invokeAction("stash-apply", stash.Ref));
        DropCommand = new AsyncRelayCommand(() => invokeAction("stash-drop", stash.Ref));
    }

    public string Message { get; }

    public string RefLabel { get; }

    public IAsyncRelayCommand PopCommand { get; }

    public IAsyncRelayCommand ApplyCommand { get; }

    public IAsyncRelayCommand DropCommand { get; }
}
