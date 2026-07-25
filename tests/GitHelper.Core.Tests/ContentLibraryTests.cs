using GitHelper.Core.Actions;
using GitHelper.Core.Content;

namespace GitHelper.Core.Tests;

public class ContentLibraryTests
{
    [Fact]
    public void Load_ReadsEmbeddedActionFiles()
    {
        var library = ContentLibrary.Load();

        var stageFile = library.Actions["stage-file"];
        Assert.Equal("Stage file", stageFile.Title);
        Assert.Equal(Danger.Safe, stageFile.Danger);
        Assert.Contains("staging-area", stageFile.Terms);
        Assert.NotEmpty(stageFile.What);
    }

    [Fact]
    public void Load_ReadsEmbeddedGlossaryFiles()
    {
        var library = ContentLibrary.Load();

        var term = library.Terms["staging-area"];
        Assert.Equal("staging area", term.Title);
        Assert.NotEmpty(term.Definition);
    }

    [Fact]
    public void Load_IsCaseInsensitiveOnIds()
    {
        var library = ContentLibrary.Load();

        Assert.True(library.Actions.ContainsKey("STAGE-FILE"));
    }
}
