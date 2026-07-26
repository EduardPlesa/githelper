using GitHelper.Core.Content;

namespace GitHelper.App.Content;

/// <summary>
/// Replaces every <see cref="SlotSpan"/> with a <see cref="TextSpan"/> carrying its bound
/// value, so the renderer never has to know about slots. Pure — no Avalonia, no I/O.
/// </summary>
public static class SlotResolver
{
    public static IReadOnlyList<ContentBlock> Resolve(
        IReadOnlyList<ContentBlock> blocks,
        IReadOnlyDictionary<string, string> slots)
        => blocks.Select(block => ResolveBlock(block, slots)).ToArray();

    private static ContentBlock ResolveBlock(
        ContentBlock block,
        IReadOnlyDictionary<string, string> slots) => block switch
    {
        ParagraphBlock paragraph => new ParagraphBlock(ResolveSpans(paragraph.Spans, slots)),

        BulletListBlock bullets => new BulletListBlock(
            bullets.Items.Select(item => ResolveSpans(item, slots)).ToArray()),

        // Code blocks are literal command text; slots are never authored inside them.
        CodeBlock code => code,

        _ => block,
    };

    private static IReadOnlyList<InlineSpan> ResolveSpans(
        IReadOnlyList<InlineSpan> spans,
        IReadOnlyDictionary<string, string> slots)
        => spans.Select(span => span switch
        {
            SlotSpan slot => slots.TryGetValue(slot.SlotName, out var value)
                ? new TextSpan(value)
                // SlotBinder.KnownSlots and the engine's content-integrity tests make this
                // unreachable through authored content. Failing loudly beats showing a
                // beginner a raw "{slotName}".
                : throw new InvalidOperationException(
                    $"Content slot '{slot.SlotName}' has no bound value."),

            _ => span,
        }).ToArray();
}
