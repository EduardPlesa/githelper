namespace GitHelper.App.Infrastructure;

/// <summary>
/// Asks the user to confirm a destructive action in a modal the app cannot click past by
/// reflex. Abstracted so the "modal only for Destructive" rule is testable without a window.
/// </summary>
public interface IConfirmationDialog
{
    Task<bool> ConfirmDestructiveAsync(string title, string consequence, CancellationToken ct = default);
}
