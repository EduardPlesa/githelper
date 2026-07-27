using Avalonia.Controls;
using GitHelper.App.Views;

namespace GitHelper.App.Infrastructure;

/// <summary>
/// The real confirmation dialog. Takes an accessor because the window does not exist when
/// the composition root builds the viewmodels.
/// </summary>
public sealed class AvaloniaConfirmationDialog(Func<Window?> ownerAccessor) : IConfirmationDialog
{
    public async Task<bool> ConfirmDestructiveAsync(
        string title, string consequence, CancellationToken ct = default)
    {
        var owner = ownerAccessor();

        // With no window there is nothing to parent a modal to. Refusing is the safe
        // default for a destructive action.
        if (owner is null) return false;

        return await DiscardConfirmationDialog.ShowAsync(owner, title, consequence);
    }
}
