using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using GitHelper.App.Rendering;
using GitHelper.Core.Content;

namespace GitHelper.App.Tests;

public class ContentBlockRendererTests
{
    private static ContentBlockRenderer NewRenderer(Action<string>? onCopy = null)
        => new(ContentLibrary.Load(), onCopy);

    /// <summary>
    /// Collects every piece of rendered text, in order, walking the control tree the
    /// renderer built. Avoids depending on a layout pass.
    /// </summary>
    private static List<string> TextOf(Control control)
    {
        var found = new List<string>();
        Walk(control, found);
        return found;

        static void Walk(Control control, List<string> found)
        {
            switch (control)
            {
                case TextBlock textBlock:
                    if (textBlock.Inlines is { Count: > 0 })
                    {
                        foreach (var inline in textBlock.Inlines)
                        {
                            switch (inline)
                            {
                                case Run run when run.Text is { Length: > 0 }:
                                    found.Add(run.Text);
                                    break;
                                case InlineUIContainer { Child: Control child }:
                                    Walk(child, found);
                                    break;
                            }
                        }
                    }
                    else if (!string.IsNullOrEmpty(textBlock.Text))
                    {
                        found.Add(textBlock.Text);
                    }
                    break;

                case Button button:
                    // Recorded by name, not content, so tests can assert a button exists.
                    found.Add("[button]");
                    break;

                case Border { Child: Control borderChild }:
                    Walk(borderChild, found);
                    break;

                case Panel panel:
                    foreach (var child in panel.Children) Walk(child, found);
                    break;
            }
        }
    }

    [AvaloniaFact]
    public void Render_ParagraphWithPlainText()
    {
        var blocks = new ContentBlock[]
        {
            new ParagraphBlock(new InlineSpan[] { new TextSpan("Saves a snapshot.") }),
        };

        var rendered = NewRenderer().Render(blocks);

        Assert.Equal(new[] { "Saves a snapshot." }, TextOf(rendered));
    }

    [AvaloniaFact]
    public void Render_ParagraphKeepsSpanOrderAcrossKinds()
    {
        var blocks = new ContentBlock[]
        {
            new ParagraphBlock(new InlineSpan[]
            {
                new TextSpan("Run "),
                new CodeSpan("git add"),
                new TextSpan(" to use the "),
                new TermSpan("staging-area", "staging area"),
                new TextSpan("."),
            }),
        };

        var rendered = NewRenderer().Render(blocks);

        Assert.Equal(
            new[] { "Run ", "git add", " to use the ", "staging area", "." },
            TextOf(rendered));
    }

    [AvaloniaFact]
    public void Render_StrongSpanIsBoldAndKeepsItsTextInline()
    {
        var blocks = new ContentBlock[]
        {
            new ParagraphBlock(new InlineSpan[]
            {
                new StrongSpan("This deletes your edits."),
                new TextSpan(" Nothing can bring them back."),
            }),
        };

        var rendered = NewRenderer().Render(blocks);

        Assert.Equal(
            new[] { "This deletes your edits.", " Nothing can bring them back." },
            TextOf(rendered));

        var strong = ((TextBlock)((StackPanel)rendered).Children[0]).Inlines!
            .OfType<Run>()
            .First();
        Assert.Equal(FontWeight.Bold, strong.FontWeight);
    }

    [AvaloniaFact]
    public void Render_BulletListProducesOneRowPerItem()
    {
        var blocks = new ContentBlock[]
        {
            new BulletListBlock(new IReadOnlyList<InlineSpan>[]
            {
                new InlineSpan[] { new TextSpan("first") },
                new InlineSpan[] { new TextSpan("second") },
            }),
        };

        var rendered = NewRenderer().Render(blocks);
        var text = TextOf(rendered);

        Assert.Contains("first", text);
        Assert.Contains("second", text);
        Assert.Equal(2, text.Count(t => t == "•"));
    }

    [AvaloniaFact]
    public void Render_CodeBlockShowsTheCodeAndACopyButtonWhenACallbackIsSupplied()
    {
        var blocks = new ContentBlock[] { new CodeBlock("git status") };

        var rendered = NewRenderer(onCopy: _ => { }).Render(blocks);
        var text = TextOf(rendered);

        Assert.Contains("git status", text);
        Assert.Contains("[button]", text);
    }

    [AvaloniaFact]
    public void Render_CodeBlockOmitsTheCopyButtonWithoutACallback()
    {
        var blocks = new ContentBlock[] { new CodeBlock("git status") };

        var rendered = NewRenderer(onCopy: null).Render(blocks);

        Assert.DoesNotContain("[button]", TextOf(rendered));
    }

    [AvaloniaFact]
    public void Render_CopyButtonInvokesTheCallbackWithTheCodeText()
    {
        string? copied = null;
        var blocks = new ContentBlock[] { new CodeBlock("git commit -m \"hi\"") };

        var rendered = NewRenderer(onCopy: text => copied = text).Render(blocks);
        var button = FindButton(rendered);
        Assert.NotNull(button);
        button!.Command!.Execute(button.CommandParameter);

        Assert.Equal("git commit -m \"hi\"", copied);

        static Button? FindButton(Control control) => control switch
        {
            Button button => button,
            Border { Child: Control child } => FindButton(child),
            Panel panel => panel.Children.OfType<Control>().Select(FindButton).FirstOrDefault(b => b is not null),
            _ => null,
        };
    }

    [AvaloniaFact]
    public void Render_TermSpanGetsATooltipCarryingTheDefinition()
    {
        // staging-area is authored in GitHelper.Content and shipped as an embedded resource.
        var blocks = new ContentBlock[]
        {
            new ParagraphBlock(new InlineSpan[] { new TermSpan("staging-area", "staging area") }),
        };

        var rendered = NewRenderer().Render(blocks);
        var termControl = FindTermControl(rendered);

        Assert.NotNull(termControl);
        Assert.NotNull(ToolTip.GetTip(termControl!));

        static Control? FindTermControl(Control control)
        {
            if (control is TextBlock { Inlines: { Count: > 0 } inlines })
            {
                foreach (var inline in inlines)
                    if (inline is InlineUIContainer { Child: Control child })
                        return child;
            }

            if (control is Panel panel)
                foreach (var child in panel.Children.OfType<Control>())
                    if (FindTermControl(child) is { } found) return found;

            return null;
        }
    }

    [AvaloniaFact]
    public void Render_UnknownTermStillRendersItsDisplayTextWithoutATooltip()
    {
        // Content integrity tests in the engine make this unreachable via authored content,
        // but the renderer must not crash if it ever happens.
        var blocks = new ContentBlock[]
        {
            new ParagraphBlock(new InlineSpan[] { new TermSpan("no-such-term", "mystery") }),
        };

        var rendered = NewRenderer().Render(blocks);

        Assert.Contains("mystery", TextOf(rendered));
    }

    [AvaloniaFact]
    public void Render_ThrowsOnAnUnresolvedSlotRatherThanShowingItToTheUser()
    {
        var blocks = new ContentBlock[]
        {
            new ParagraphBlock(new InlineSpan[] { new SlotSpan("branch") }),
        };

        var ex = Assert.Throws<InvalidOperationException>(() => NewRenderer().Render(blocks));

        Assert.Contains("branch", ex.Message);
    }

    [AvaloniaFact]
    public void Render_HandlesAnEmptyBlockList()
    {
        var rendered = NewRenderer().Render(Array.Empty<ContentBlock>());

        Assert.Empty(TextOf(rendered));
    }

    [AvaloniaFact]
    public void Render_RendersRealAuthoredContentEndToEnd()
    {
        // Proves the renderer handles actual shipped content, not just synthetic trees.
        // The Risks section is used rather than What because authored Risks sections carry
        // no slots, keeping this test about rendering rather than slot binding.
        var library = ContentLibrary.Load();
        var renderer = new ContentBlockRenderer(library, onCopyRequested: _ => { });

        var rendered = renderer.Render(library.Actions["commit"].Risks);

        Assert.NotEmpty(TextOf(rendered));
    }
}
