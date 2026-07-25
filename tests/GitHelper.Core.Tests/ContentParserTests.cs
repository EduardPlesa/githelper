using GitHelper.Core.Actions;
using GitHelper.Core.Content;

namespace GitHelper.Core.Tests;

public class ContentParserTests
{
    private const string Minimal = """
        ---
        id: commit
        title: Commit
        danger: caution
        terms: [staging-area, commit]
        undo: undo-last-commit
        ---
        ## what
        Saves a snapshot.

        ## risks
        Nothing serious.

        ## undo
        Use undo last commit.
        """;

    [Fact]
    public void Parse_ReadsFrontmatter()
    {
        var doc = ContentParser.Parse(Minimal, "commit.md");

        Assert.Equal("commit", doc.Id);
        Assert.Equal("Commit", doc.Title);
        Assert.Equal(Danger.Caution, doc.Danger);
        Assert.Equal(new[] { "staging-area", "commit" }, doc.Terms);
        Assert.Equal("undo-last-commit", doc.UndoActionId);
    }

    [Fact]
    public void Parse_SplitsTheThreeSections()
    {
        var doc = ContentParser.Parse(Minimal, "commit.md");

        Assert.Single(doc.What);
        Assert.Single(doc.Risks);
        Assert.Single(doc.Undo);
    }

    [Fact]
    public void Parse_ReadsParagraphsBulletsAndCodeBlocks()
    {
        var text = """
            ---
            id: x
            title: X
            danger: safe
            ---
            ## what
            A paragraph.

            - first bullet
            - second bullet

            ```
            git status
            ```

            ## risks
            None.

            ## undo
            Nothing to undo.
            """;

        var doc = ContentParser.Parse(text, "x.md");

        Assert.Equal(3, doc.What.Count);
        Assert.IsType<ParagraphBlock>(doc.What[0]);

        var bullets = Assert.IsType<BulletListBlock>(doc.What[1]);
        Assert.Equal(2, bullets.Items.Count);

        var code = Assert.IsType<CodeBlock>(doc.What[2]);
        Assert.Equal("git status", code.Text);
    }

    [Fact]
    public void Parse_ReadsInlineCodeTermsAndSlots()
    {
        var text = """
            ---
            id: x
            title: X
            danger: safe
            ---
            ## what
            Run `git add` on {stagedCount} files in the [[staging-area|staging area]] on [[HEAD]].

            ## risks
            None.

            ## undo
            None.
            """;

        var doc = ContentParser.Parse(text, "x.md");
        var spans = Assert.IsType<ParagraphBlock>(doc.What[0]).Spans;

        Assert.Contains(spans, s => s is CodeSpan { Text: "git add" });
        Assert.Contains(spans, s => s is SlotSpan { SlotName: "stagedCount" });
        Assert.Contains(spans, s => s is TermSpan { TermId: "staging-area", Display: "staging area" });
        // With no display text the id is shown verbatim.
        Assert.Contains(spans, s => s is TermSpan { TermId: "HEAD", Display: "HEAD" });
    }

    [Fact]
    public void Parse_TreatsUndoAsOptionalOnlyInFrontmatter()
    {
        var text = """
            ---
            id: x
            title: X
            danger: safe
            ---
            ## what
            Something.

            ## risks
            None.

            ## undo
            None needed.
            """;

        var doc = ContentParser.Parse(text, "x.md");

        Assert.Null(doc.UndoActionId);
        Assert.NotEmpty(doc.Undo);
    }

    [Theory]
    [InlineData("no frontmatter at all", "frontmatter")]
    [InlineData("---\nid: x\ntitle: X\ndanger: safe\n---\n## what\nOnly one section.", "risks")]
    [InlineData("---\n---\n## what\nA.\n\n## risks\nB.\n\n## undo\nC.\n", "empty")]
    [InlineData("---\nid: x\ntitle: X\ndanger: safe\n## what\nA.\n\n## risks\nB.\n\n## undo\nC.\n", "closed")]
    [InlineData("---\nid: x\n  bad: [unclosed\n---\n## what\nA.\n\n## risks\nB.\n\n## undo\nC.\n", "yaml")]
    public void Parse_RejectsMalformedContent(string text, string expectedMessageFragment)
    {
        var ex = Assert.Throws<ContentException>(() => ContentParser.Parse(text, "bad.md"));

        Assert.Contains(expectedMessageFragment, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bad.md", ex.Message);
    }

    [Fact]
    public void Parse_RejectsUnknownSectionRatherThanDroppingIt()
    {
        var text = """
            ---
            id: x
            title: X
            danger: safe
            ---
            ## what
            A.

            ## risks
            B.

            ## undo
            C.

            ## surprise
            D.
            """;

        var ex = Assert.Throws<ContentException>(() => ContentParser.Parse(text, "x.md"));

        Assert.Contains("surprise", ex.Message);
    }
}
