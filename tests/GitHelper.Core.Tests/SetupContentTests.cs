using GitHelper.Core.Content;

namespace GitHelper.Core.Tests;

public class SetupContentTests
{
    private static readonly ContentLibrary Library = ContentLibrary.Load();

    [Theory]
    [InlineData("init-repository")]
    [InlineData("create-gitignore")]
    public void SetupOperationsHaveContent(string id)
    {
        Assert.True(Library.Setup.ContainsKey(id), $"no setup content for '{id}'");
    }

    [Theory]
    [InlineData("init-repository")]
    [InlineData("create-gitignore")]
    public void SetupContentFillsAllFourHeadings(string id)
    {
        var document = Library.Setup[id];

        Assert.NotEmpty(document.What);
        Assert.NotEmpty(document.Risks);
        Assert.NotEmpty(document.Undo);
        Assert.False(string.IsNullOrWhiteSpace(document.Title));
    }

    [Fact]
    public void SetupContentUsesNoSlots()
    {
        // SlotBinder.Bind needs a RepoState, which does not exist before `git init`. A slot
        // here would reach the renderer unresolved and throw.
        var offenders = Library.Setup.Values
            .SelectMany(d => d.What.Concat(d.Risks).Concat(d.Undo))
            .OfType<ParagraphBlock>()
            .SelectMany(p => p.Spans)
            .OfType<SlotSpan>()
            .Select(s => s.SlotName)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void SetupContentIsNotAlsoLoadedAsAnAction()
    {
        // Otherwise EveryContentFileMatchesARealAction would fail: these have no catalogue entry.
        Assert.False(Library.Actions.ContainsKey("init-repository"));
        Assert.False(Library.Actions.ContainsKey("create-gitignore"));
    }
}
