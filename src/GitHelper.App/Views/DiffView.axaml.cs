using Avalonia.Controls;
using GitHelper.App.Rendering;
using GitHelper.App.ViewModels;
using GitHelper.Core.Content;

namespace GitHelper.App.Views;

public partial class DiffView : UserControl
{
    // Loaded once: the content is embedded in the assembly and never changes at runtime.
    // The same arrangement ExplainPanelView uses.
    private static readonly ContentLibrary Library = ContentLibrary.Load();

    public DiffView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => RenderIntro();
    }

    /// <summary>
    /// The block schema maps to a control tree, which bindings cannot build from a list of
    /// records without a converter per block type — so it is rendered in code, exactly as
    /// ExplainPanelView renders its four sections.
    ///
    /// No PropertyChanged subscription, unlike that panel: the intro is one fixed document
    /// about how to read a diff, not about the file being read, so it never changes once the
    /// viewmodel is attached.
    /// </summary>
    private void RenderIntro()
    {
        var host = this.FindControl<StackPanel>("IntroHost");
        if (host is null) return;

        host.Children.Clear();

        var blocks = (DataContext as DiffViewModel)?.IntroBlocks;
        if (blocks is null || blocks.Count == 0) return;

        // No copy callback: this document is prose about how to read a diff and carries no
        // code block, so there is nothing here for a Copy button to copy.
        host.Children.Add(new ContentBlockRenderer(Library).Render(blocks));
    }
}
