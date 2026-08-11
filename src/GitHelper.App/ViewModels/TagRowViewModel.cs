using CommunityToolkit.Mvvm.Input;
using GitHelper.Core.Model;

namespace GitHelper.App.ViewModels;

/// <summary>One tag in the Branches view.</summary>
public sealed class TagRowViewModel : ViewModelBase
{
    public TagRowViewModel(TagInfo tag, Func<string, string, Task> invokeAction)
    {
        Name = tag.Name;
        TargetLabel = tag.Target;

        DeleteCommand = new AsyncRelayCommand(() => invokeAction("delete-tag", tag.Name));
    }

    public string Name { get; }

    public string TargetLabel { get; }

    public IAsyncRelayCommand DeleteCommand { get; }
}
