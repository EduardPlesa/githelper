using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using GitHelper.Core.Content;

namespace GitHelper.App.Rendering;

/// <summary>
/// Turns the engine's closed content-block schema into Avalonia controls.
///
/// The schema is closed on purpose, so this renderer handles every case exhaustively and
/// never has to guess. Anything it cannot render is a bug upstream, not something to
/// silently drop.
/// </summary>
public sealed class ContentBlockRenderer(
    ContentLibrary content,
    Action<string>? onCopyRequested = null)
{
    private static readonly FontFamily MonospaceFont =
        new("Consolas, Cascadia Mono, Courier New, monospace");

    private const double BlockSpacing = 8;

    public Control Render(IReadOnlyList<ContentBlock> blocks)
        => Render(blocks, allowTermTooltips: true);

    private Control Render(IReadOnlyList<ContentBlock> blocks, bool allowTermTooltips)
    {
        var panel = new StackPanel { Spacing = BlockSpacing };

        foreach (var block in blocks)
            panel.Children.Add(RenderBlock(block, allowTermTooltips));

        return panel;
    }

    private Control RenderBlock(ContentBlock block, bool allowTermTooltips) => block switch
    {
        ParagraphBlock paragraph => RenderParagraph(paragraph.Spans, allowTermTooltips),
        BulletListBlock bullets => RenderBulletList(bullets, allowTermTooltips),
        CodeBlock code => RenderCodeBlock(code),

        // The schema is closed; a new case here means the engine added a block type
        // and this renderer was not updated with it.
        _ => throw new InvalidOperationException(
            $"No renderer for content block type '{block.GetType().Name}'."),
    };

    private TextBlock RenderParagraph(IReadOnlyList<InlineSpan> spans, bool allowTermTooltips)
    {
        var textBlock = new TextBlock { TextWrapping = TextWrapping.Wrap };

        foreach (var span in spans)
            textBlock.Inlines!.Add(RenderSpan(span, allowTermTooltips));

        return textBlock;
    }

    private Control RenderBulletList(BulletListBlock bullets, bool allowTermTooltips)
    {
        // A StackPanel of pre-built rows rather than an ItemsControl: no item template and
        // no layout pass needed before the content exists.
        var list = new StackPanel { Spacing = 4 };

        foreach (var item in bullets.Items)
        {
            var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(new TextBlock { Text = "•", VerticalAlignment = VerticalAlignment.Top });
            row.Children.Add(RenderParagraph(item, allowTermTooltips));
            list.Children.Add(row);
        }

        return list;
    }

    private Control RenderCodeBlock(CodeBlock code)
    {
        var text = new TextBlock
        {
            Text = code.Text,
            FontFamily = MonospaceFont,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var layout = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
        };
        layout.Children.Add(text);

        if (onCopyRequested is { } copy)
        {
            layout.Children.Add(new Button
            {
                Content = "Copy",
                VerticalAlignment = VerticalAlignment.Center,
                Command = new RelayCommand(() => copy(code.Text)),
            });
        }

        return new Border
        {
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(20, 128, 128, 128)),
            Child = layout,
        };
    }

    private Inline RenderSpan(InlineSpan span, bool allowTermTooltips) => span switch
    {
        TextSpan text => new Run(text.Text),

        CodeSpan code => new Run(code.Text)
        {
            FontFamily = MonospaceFont,
            Background = new SolidColorBrush(Color.FromArgb(20, 128, 128, 128)),
        },

        StrongSpan strong => new Run(strong.Text) { FontWeight = FontWeight.Bold },

        TermSpan term => new InlineUIContainer(RenderTerm(term, allowTermTooltips)),

        // SlotBinder resolves slots before content reaches a view. Reaching here means a
        // viewmodel skipped that step, and rendering "{branch}" to a beginner would be worse
        // than failing loudly during development.
        SlotSpan slot => throw new InvalidOperationException(
            $"Unresolved content slot '{slot.SlotName}' reached the renderer. "
            + "Bind slots with SlotBinder before rendering."),

        _ => throw new InvalidOperationException(
            $"No renderer for inline span type '{span.GetType().Name}'."),
    };

    private Control RenderTerm(TermSpan term, bool allowTermTooltips)
    {
        var control = new TextBlock
        {
            Text = term.Display,
            TextDecorations = TextDecorations.Underline,
        };

        if (allowTermTooltips && content.Terms.TryGetValue(term.TermId, out var glossaryTerm))
            ToolTip.SetTip(control, BuildTermTooltip(glossaryTerm));

        return control;
    }

    private Control BuildTermTooltip(GlossaryTerm term)
    {
        var panel = new StackPanel { Spacing = 4, MaxWidth = 320 };

        panel.Children.Add(new TextBlock
        {
            Text = term.Title,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
        });

        // allowTermTooltips: false — a definition may mention another term whose definition
        // mentions this one, and nesting tooltips would build an unbounded control tree.
        panel.Children.Add(Render(term.Definition, allowTermTooltips: false));

        return panel;
    }
}
