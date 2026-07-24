using GitHelper.Core.Parsing;

namespace GitHelper.Core.Tests;

public class LogParserTests
{
    private const string Unit = "";
    private const string Record = "";

    [Fact]
    public void Parse_ReadsAllCommitFields()
    {
        var input =
            $"a1b2c3d4e5f6{Unit}a1b2c3d{Unit}Ada Lovelace{Unit}2026-07-24T10:30:00+02:00{Unit}Add the thing{Record}";

        var commits = LogParser.Parse(input);

        var commit = Assert.Single(commits);
        Assert.Equal("a1b2c3d4e5f6", commit.Hash);
        Assert.Equal("a1b2c3d", commit.ShortHash);
        Assert.Equal("Ada Lovelace", commit.Author);
        Assert.Equal("Add the thing", commit.Subject);
        Assert.Equal(new DateTimeOffset(2026, 7, 24, 10, 30, 0, TimeSpan.FromHours(2)), commit.Date);
    }

    [Fact]
    public void Parse_ReadsMultipleCommitsInOrder()
    {
        var input =
            $"h2{Unit}h2{Unit}B{Unit}2026-07-24T10:00:00+00:00{Unit}second{Record}" +
            $"h1{Unit}h1{Unit}A{Unit}2026-07-23T10:00:00+00:00{Unit}first{Record}";

        var commits = LogParser.Parse(input);

        Assert.Equal(2, commits.Count);
        Assert.Equal("second", commits[0].Subject);
        Assert.Equal("first", commits[1].Subject);
    }

    [Fact]
    public void Parse_PreservesSubjectsContainingTabsAndNewlines()
    {
        var subject = "fix:\ttabbed\nand newlined";
        var input = $"h{Unit}h{Unit}A{Unit}2026-07-24T10:00:00+00:00{Unit}{subject}{Record}";

        var commits = LogParser.Parse(input);

        Assert.Equal(subject, Assert.Single(commits).Subject);
    }

    [Fact]
    public void Parse_HandlesEmptyOutputFromRepoWithNoCommits()
    {
        Assert.Empty(LogParser.Parse(""));
    }

    [Fact]
    public async Task Parse_MatchesRealGitOutput()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("second.txt", "x\n");
        await repo.GitAsync("add", "-A");
        await repo.GitAsync("commit", "-q", "-m", "second commit");

        var result = await repo.GitAsync("log", "--format=" + LogParser.Format, "-n", "50");
        var commits = LogParser.Parse(result.StdOut);

        Assert.Equal(2, commits.Count);
        Assert.Equal("second commit", commits[0].Subject);
        Assert.Equal("initial", commits[1].Subject);
        Assert.Equal("Test User", commits[0].Author);
    }
}
