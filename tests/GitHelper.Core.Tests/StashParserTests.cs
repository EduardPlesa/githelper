using GitHelper.Core.Parsing;

namespace GitHelper.Core.Tests;

public class StashParserTests
{
    [Fact]
    public void Parse_ReadsRefAndMessage()
    {
        var input = "stash@{0}\tWIP on main: abc1234 first message\nstash@{1}\tOn main: second\n";

        var stashes = StashParser.Parse(input);

        Assert.Equal(2, stashes.Count);
        Assert.Equal("stash@{0}", stashes[0].Ref);
        Assert.Equal("WIP on main: abc1234 first message", stashes[0].Message);
        Assert.Equal("stash@{1}", stashes[1].Ref);
        Assert.Equal("On main: second", stashes[1].Message);
    }

    [Fact]
    public void Parse_HandlesEmptyOutput()
    {
        Assert.Empty(StashParser.Parse(""));
    }

    [Fact]
    public void Parse_KeepsAnyExtraTabsAsPartOfTheMessage()
    {
        // The subject is freeform text; only the first tab is the field separator.
        var input = "stash@{0}\tmessage\twith\ttabs\n";

        var stashes = StashParser.Parse(input);

        Assert.Equal("message\twith\ttabs", Assert.Single(stashes).Message);
    }

    [Fact]
    public async Task Parse_MatchesRealGitOutput()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("README.md", "changed\n");
        await repo.GitAsync("stash", "push", "-m", "wip");

        var result = await repo.GitAsync("stash", "list", "--format=" + StashParser.Format);
        var stashes = StashParser.Parse(result.StdOut);

        Assert.Single(stashes);
        Assert.StartsWith("stash@{0}", stashes[0].Ref);
        Assert.Contains("wip", stashes[0].Message);
    }
}
