using Avalonia.Controls;
using Avalonia.Interactivity;
using GitHelper.App.ViewModels;

namespace GitHelper.App.Views;

public partial class CommandLogView : UserControl
{
    public CommandLogView() => InitializeComponent();

    private void OnCopyAll(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CommandLogViewModel viewModel) return;

        _ = TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(viewModel.ClipboardText);
    }
}
