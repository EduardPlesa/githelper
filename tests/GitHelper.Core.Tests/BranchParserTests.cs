using GitHelper.Core.Parsing;

namespace GitHelper.Core.Tests;

public class BranchParserTests
{
    [Fact]
    public void Parse_ReadsNameAndUpstream()
    {
        var input = "main\torigin/main\nfeature\t\n";

        var branches = BranchParser.Parse(input);

        Assert.Equal(2, branches.Count);
        Assert.Equal("main", branches[0].Name);
        Assert.Equal("origin/main", branches[0].Upstream);
        Assert.Equal("feature", branches[1].Name);
        Assert.Null(branches[1].Upstream);
    }

    [Fact]
    public void Parse_ReadsSlashedBranchNames()
    {
        var input = "feature/add-login\t\n";

        Assert.Equal("feature/add-login", Assert.Single(BranchParser.Parse(input)).Name);
    }

    [Fact]
    public void Parse_HandlesEmptyOutput()
    {
        Assert.Empty(BranchParser.Parse(""));
    }

    [Fact]
    public async Task Parse_MatchesRealGitOutput()
    {
        using var repo = await TestRepo.CreateAsync();
        await repo.GitAsync("branch", "feature");

        var result = await repo.GitAsync("for-each-ref", "--format=" + BranchParser.Format, "refs/heads/");
        var branches = BranchParser.Parse(result.StdOut);

        Assert.Equal(2, branches.Count);
        Assert.Contains(branches, b => b.Name == "main" && b.Upstream is null);
        Assert.Contains(branches, b => b.Name == "feature");
    }
}
