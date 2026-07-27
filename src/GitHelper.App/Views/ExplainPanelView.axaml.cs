using System.ComponentModel;
using Avalonia.Controls;
using GitHelper.App.Rendering;
using GitHelper.App.ViewModels;
using GitHelper.Core.Content;

namespace GitHelper.App.Views;

public partial class ExplainPanelView : UserControl
{
    // Loaded once: the content is embedded in the assembly and never changes at runtime.
    private static readonly ContentLibrary Library = ContentLibrary.Load();

    private ExplainPanelViewModel? _viewModel;

    public ExplainPanelView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = DataContext as ExplainPanelViewModel;

        if (_viewModel is not null) _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        RenderBlocks();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ExplainPanelViewModel.WhatBlocks)
            or nameof(ExplainPanelViewModel.RisksBlocks)
            or nameof(ExplainPanelViewModel.UndoBlocks))
        {
            RenderBlocks();
        }
    }

    /// <summary>
    /// The block schema maps to a control tree, which bindings cannot build from a list of
    /// records without a converter per block type — so it is rendered in code instead.
    /// </summary>
    private void RenderBlocks()
    {
        var renderer = new ContentBlockRenderer(Library, CopyToClipboard);

        Fill("WhatHost", _viewModel?.WhatBlocks, renderer);
        Fill("RisksHost", _viewModel?.RisksBlocks, renderer);
        Fill("UndoHost", _viewModel?.UndoBlocks, renderer);
    }

    private void Fill(string hostName, IReadOnlyList<ContentBlock>? blocks, ContentBlockRenderer renderer)
    {
        var host = this.FindControl<StackPanel>(hostName);
        if (host is null) return;

        host.Children.Clear();
        if (blocks is null || blocks.Count == 0) return;

        host.Children.Add(renderer.Render(blocks));
    }

    private void CopyToClipboard(string text)
        => _ = TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(text);
}
