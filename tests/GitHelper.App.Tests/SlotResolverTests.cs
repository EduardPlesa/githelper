using GitHelper.App.Content;
using GitHelper.Core.Content;

namespace GitHelper.App.Tests;

public class SlotResolverTests
{
    private static readonly Dictionary<string, string> Slots = new()
    {
        ["branch"] = "main",
        ["stagedCount"] = "3",
    };

    private static IReadOnlyList<string> SpanTexts(ContentBlock block)
        => ((ParagraphBlock)block).Spans.Cast<TextSpan>().Select(s => s.Text).ToArray();

    [Fact]
    public void Resolve_ReplacesASlotWithItsBoundValue()
    {
        var blocks = new ContentBlock[]
        {
            new ParagraphBlock(new InlineSpan[]
            {
                new TextSpan("You are on "),
                new SlotSpan("branch"),
                new TextSpan("."),
            }),
        };

        var resolved = SlotResolver.Resolve(blocks, Slots);

        Assert.Equal(new[] { "You are on ", "main", "." }, SpanTexts(resolved[0]));
    }

    [Fact]
    public void Resolve_LeavesOtherSpanKindsUntouched()
    {
        var blocks = new ContentBlock[]
        {
            new ParagraphBlock(new InlineSpan[]
            {
                new CodeSpan("git add"),
                new TermSpan("staging-area", "staging area"),
            }),
        };

        var resolved = SlotResolver.Resolve(blocks, Slots);
        var spans = ((ParagraphBlock)resolved[0]).Spans;

        Assert.IsType<CodeSpan>(spans[0]);
        Assert.IsType<TermSpan>(spans[1]);
    }

    [Fact]
    public void Resolve_HandlesSlotsInsideBulletItems()
    {
        var blocks = new ContentBlock[]
        {
            new BulletListBlock(new IReadOnlyList<InlineSpan>[]
            {
                new InlineSpan[] { new SlotSpan("stagedCount"), new TextSpan(" files") },
            }),
        };

        var resolved = SlotResolver.Resolve(blocks, Slots);
        var item = ((BulletListBlock)resolved[0]).Items[0];

        Assert.Equal("3", ((TextSpan)item[0]).Text);
        Assert.Equal(" files", ((TextSpan)item[1]).Text);
    }

    [Fact]
    public void Resolve_PassesCodeBlocksThroughUnchanged()
    {
        var blocks = new ContentBlock[] { new CodeBlock("git status") };

        var resolved = SlotResolver.Resolve(blocks, Slots);

        Assert.Equal("git status", ((CodeBlock)resolved[0]).Text);
    }

    [Fact]
    public void Resolve_ThrowsNamingAnUnboundSlotRatherThanRenderingItRaw()
    {
        var blocks = new ContentBlock[]
        {
            new ParagraphBlock(new InlineSpan[] { new SlotSpan("notBound") }),
        };

        var ex = Assert.Throws<InvalidOperationException>(() => SlotResolver.Resolve(blocks, Slots));

        Assert.Contains("notBound", ex.Message);
    }
}
