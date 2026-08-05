using GitHelper.Core.Parsing;

namespace GitHelper.Core.Tests;

public class TagParserTests
{
    [Fact]
    public void Parse_ReadsNameAndTarget()
    {
        var input = "v1\tabc1234\nv2\tdef5678\n";

        var tags = TagParser.Parse(input);

        Assert.Equal(2, tags.Count);
        Assert.Equal("v1", tags[0].Name);
        Assert.Equal("abc1234", tags[0].Target);
        Assert.Equal("v2", tags[1].Name);
        Assert.Equal("def5678", tags[1].Target);
    }

    [Fact]
    public void Parse_HandlesEmptyOutput()
    {
        Assert.Empty(TagParser.Parse(""));
    }

    [Fact]
    public async Task Parse_MatchesRealGitOutput()
    {
        using var repo = await TestRepo.CreateAsync();
        await repo.GitAsync("tag", "v1");

        var result = await repo.GitAsync("for-each-ref", "--format=" + TagParser.Format, "refs/tags/");
        var tags = TagParser.Parse(result.StdOut);

        Assert.Single(tags);
        Assert.Equal("v1", tags[0].Name);
        Assert.NotEmpty(tags[0].Target);
    }
}
