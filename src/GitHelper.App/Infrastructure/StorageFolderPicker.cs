using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace GitHelper.App.Infrastructure;

/// <summary>
/// The real picker. Takes an accessor rather than a TopLevel because the window does not
/// exist yet when the composition root builds the viewmodels.
/// </summary>
public sealed class StorageFolderPicker(Func<TopLevel?> topLevelAccessor) : IFolderPicker
{
    public async Task<string?> PickFolderAsync(string title, CancellationToken ct = default)
    {
        var topLevel = topLevelAccessor();
        if (topLevel is null) return null;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = title, AllowMultiple = false });

        // TryGetLocalPath returns null for non-filesystem locations, which git cannot use.
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
