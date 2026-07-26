namespace GitHelper.App.Infrastructure;

/// <summary>Asks the user to choose a folder. Returns null if they cancel.</summary>
public interface IFolderPicker
{
    Task<string?> PickFolderAsync(string title, CancellationToken ct = default);
}
