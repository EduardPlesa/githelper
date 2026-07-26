using CommunityToolkit.Mvvm.Input;

namespace GitHelper.App.ViewModels;

/// <summary>One entry in the startup screen's recent-repositories list.</summary>
public sealed class RecentRepoViewModel : ViewModelBase
{
    public RecentRepoViewModel(string fullPath, Func<string, Task> open, Action<string> remove)
    {
        FullPath = fullPath;

        // The folder name is what a user recognises; the full path is shown as a subtitle.
        var trimmed = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        Name = string.IsNullOrEmpty(name) ? trimmed : name;

        OpenCommand = new AsyncRelayCommand(() => open(fullPath));
        RemoveCommand = new RelayCommand(() => remove(fullPath));
    }

    public string FullPath { get; }

    public string Name { get; }

    public IAsyncRelayCommand OpenCommand { get; }

    public IRelayCommand RemoveCommand { get; }
}
