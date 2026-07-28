using GitHelper.Core.Actions;
using GitHelper.Core.Content;

namespace GitHelper.Core.Tests;

/// <summary>
/// Keeps authored content and code from drifting apart. Every failure here is a content
/// bug that would otherwise surface as a blank or wrong explanation panel.
/// </summary>
public class ContentIntegrityTests
{
    private static readonly ContentLibrary Library = ContentLibrary.Load();

    private static IEnumerable<ExplanationDocument> AllDocuments()
        => Library.Actions.Values.Concat(Library.Setup.Values);

    private static IEnumerable<InlineSpan> AllSpans(ExplanationDocument document)
        => Spans(document.What).Concat(Spans(document.Risks)).Concat(Spans(document.Undo));

    private static IEnumerable<InlineSpan> Spans(IEnumerable<ContentBlock> blocks)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    foreach (var span in paragraph.Spans) yield return span;
                    break;
                case BulletListBlock bullets:
                    foreach (var item in bullets.Items)
                        foreach (var span in item) yield return span;
                    break;
            }
        }
    }

    [Fact]
    public void EveryActionHasAContentFile()
    {
        var missing = ActionCatalog.All
            .Where(a => !Library.Actions.ContainsKey(a.ExplanationId))
            .Select(a => a.Id)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void EveryContentFileMatchesARealAction()
    {
        var orphans = Library.Actions.Keys
            .Where(id => ActionCatalog.Find(id) is null)
            .ToList();

        Assert.Empty(orphans);
    }

    [Fact]
    public void EveryDeclaredTermResolvesToAGlossaryFile()
    {
        var unresolved = AllDocuments()
            .SelectMany(d => d.Terms.Select(t => (Document: d.Id, Term: t)))
            .Where(x => !Library.Terms.ContainsKey(x.Term))
            .ToList();

        Assert.Empty(unresolved);
    }

    [Fact]
    public void EveryInlineTermReferenceResolvesToAGlossaryFile()
    {
        var unresolved = AllDocuments()
            .SelectMany(d => AllSpans(d).OfType<TermSpan>().Select(s => (Document: d.Id, s.TermId)))
            .Where(x => !Library.Terms.ContainsKey(x.TermId))
            .ToList();

        Assert.Empty(unresolved);
    }

    [Fact]
    public void EverySlotIsInTheKnownVocabulary()
    {
        var unknown = AllDocuments()
            .SelectMany(d => AllSpans(d).OfType<SlotSpan>().Select(s => (Document: d.Id, s.SlotName)))
            .Where(x => !SlotBinder.KnownSlots.Contains(x.SlotName))
            .ToList();

        Assert.Empty(unknown);
    }

    [Fact]
    public void EveryDestructiveActionExplainsHowToUndoIt()
    {
        foreach (var action in ActionCatalog.All.Where(a => a.Danger == Danger.Destructive))
        {
            var document = Library.Actions[action.ExplanationId];
            Assert.NotEmpty(document.Undo);
        }
    }

    [Fact]
    public void EveryActionExplainsWhatItDoesAndWhatCouldGoWrong()
    {
        foreach (var action in ActionCatalog.All)
        {
            var document = Library.Actions[action.ExplanationId];
            Assert.NotEmpty(document.What);
            Assert.NotEmpty(document.Risks);
            Assert.NotEmpty(document.Undo);
        }
    }

    [Fact]
    public void FrontmatterDangerMatchesTheActionDescriptor()
    {
        foreach (var action in ActionCatalog.All)
        {
            var document = Library.Actions[action.ExplanationId];
            Assert.Equal(action.Danger, document.Danger);
        }
    }

    [Fact]
    public void FrontmatterUndoMatchesTheActionDescriptor()
    {
        foreach (var action in ActionCatalog.All)
        {
            var document = Library.Actions[action.ExplanationId];
            Assert.Equal(action.UndoActionId, document.UndoActionId);
        }
    }

    [Fact]
    public void EveryGlossaryTermIsActuallyReferencedSomewhere()
    {
        var referenced = AllDocuments()
            .SelectMany(d => d.Terms.Concat(AllSpans(d).OfType<TermSpan>().Select(s => s.TermId)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unused = Library.Terms.Keys.Where(id => !referenced.Contains(id)).ToList();

        Assert.Empty(unused);
    }
}
