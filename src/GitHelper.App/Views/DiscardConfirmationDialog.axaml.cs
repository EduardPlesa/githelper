using Avalonia.Controls;
using Avalonia.Interactivity;

namespace GitHelper.App.Views;

/// <summary>
/// The one modal in the app. Used only for Destructive actions (discard-file, stash-drop),
/// so its confirmation cannot be clicked past by the muscle memory built up on Caution actions.
/// "Keep my changes" is the default button — the safe choice wins an accidental Enter.
/// </summary>
public partial class DiscardConfirmationDialog : Window
{
    public DiscardConfirmationDialog() => InitializeComponent();

    /// <summary>Exposed for tests, which assert the copy reaches the dialog.</summary>
    public string ConsequenceText { get; private set; } = string.Empty;

    public void SetContent(string title, string consequence)
    {
        ConsequenceText = consequence;

        var titleText = this.FindControl<TextBlock>("TitleText");
        if (titleText is not null) titleText.Text = title;

        var consequenceLabel = this.FindControl<TextBlock>("ConsequenceLabel");
        if (consequenceLabel is not null) consequenceLabel.Text = consequence;
    }

    public static async Task<bool> ShowAsync(Window owner, string title, string consequence)
    {
        var dialog = new DiscardConfirmationDialog();
        dialog.SetContent(title, consequence);

        return await dialog.ShowDialog<bool>(owner);
    }

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
