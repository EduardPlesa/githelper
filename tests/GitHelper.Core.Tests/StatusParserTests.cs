using GitHelper.Core.Model;
using GitHelper.Core.Parsing;

namespace GitHelper.Core.Tests;

public class StatusParserTests
{
    /// <summary>Builds porcelain v2 -z output: every record is NUL-terminated.</summary>
    private static string Z(params string[] records)
        => string.Concat(records.Select(r => r + "\0"));

    [Fact]
    public void Parse_ReadsBranchUpstreamAndAheadBehind()
    {
        var input = Z(
            "# branch.oid a1b2c3d",
            "# branch.head main",
            "# branch.upstream origin/main",
            "# branch.ab +2 -3");

        var snapshot = StatusParser.Parse(input);

        Assert.Equal("main", snapshot.Branch);
        Assert.False(snapshot.IsDetached);
        Assert.True(snapshot.HasCommits);
        Assert.Equal("origin/main", snapshot.Upstream);
        Assert.Equal(2, snapshot.Ahead);
        Assert.Equal(3, snapshot.Behind);
    }

    [Fact]
    public void Parse_DetectsRepositoryWithNoCommits()
    {
        var input = Z("# branch.oid (initial)", "# branch.head main");

        var snapshot = StatusParser.Parse(input);

        Assert.False(snapshot.HasCommits);
        Assert.Equal("main", snapshot.Branch);
    }

    [Fact]
    public void Parse_DetectsDetachedHead()
    {
        var input = Z("# branch.oid a1b2c3d", "# branch.head (detached)");

        var snapshot = StatusParser.Parse(input);

        Assert.True(snapshot.IsDetached);
        Assert.Null(snapshot.Branch);
    }

    [Fact]
    public void Parse_ReadsStagedAndUnstagedStatusSeparately()
    {
        var input = Z(
            "# branch.oid a1b2c3d",
            "# branch.head main",
            "1 M. N... 100644 100644 100644 aaa bbb staged-only.txt",
            "1 .M N... 100644 100644 100644 aaa bbb worktree-only.txt",
            "1 MM N... 100644 100644 100644 aaa bbb both.txt");

        var snapshot = StatusParser.Parse(input);

        var stagedOnly = snapshot.Changes.Single(c => c.Path == "staged-only.txt");
        Assert.True(stagedOnly.IsStaged);
        Assert.False(stagedOnly.HasUnstagedChanges);

        var worktreeOnly = snapshot.Changes.Single(c => c.Path == "worktree-only.txt");
        Assert.False(worktreeOnly.IsStaged);
        Assert.True(worktreeOnly.HasUnstagedChanges);

        var both = snapshot.Changes.Single(c => c.Path == "both.txt");
        Assert.True(both.IsStaged);
        Assert.True(both.HasUnstagedChanges);
    }

    [Fact]
    public void Parse_ReadsRenameRecordAndItsTrailingOriginalPath()
    {
        // The original path is its own NUL-terminated field after the rename record.
        var input = Z(
            "# branch.oid a1b2c3d",
            "# branch.head main",
            "2 R. N... 100644 100644 100644 aaa bbb R100 new-name.txt",
            "old-name.txt",
            "? untracked-after-rename.txt");

        var snapshot = StatusParser.Parse(input);

        var renamed = snapshot.Changes.Single(c => c.Path == "new-name.txt");
        Assert.Equal(ChangeKind.Renamed, renamed.IndexChange);
        Assert.Equal("old-name.txt", renamed.OriginalPath);

        // Proves the extra field was consumed rather than parsed as its own record.
        Assert.Contains(snapshot.Changes, c => c.Path == "untracked-after-rename.txt");
        Assert.Equal(2, snapshot.Changes.Count);
    }

    [Fact]
    public void Parse_HandlesUntrackedUnmergedAndIgnoredRecords()
    {
        var input = Z(
            "# branch.oid a1b2c3d",
            "# branch.head main",
            "? new.txt",
            "u UU N... 100644 100644 100644 100644 aaa bbb ccc conflicted.txt",
            "! ignored.txt");

        var snapshot = StatusParser.Parse(input);

        Assert.Equal(ChangeKind.Untracked, snapshot.Changes.Single(c => c.Path == "new.txt").WorkTreeChange);
        Assert.Equal(ChangeKind.Unmerged, snapshot.Changes.Single(c => c.Path == "conflicted.txt").WorkTreeChange);
        Assert.DoesNotContain(snapshot.Changes, c => c.Path == "ignored.txt");
    }

    [Fact]
    public void Parse_PreservesPathsWithSpacesAndNonAsciiCharacters()
    {
        var input = Z(
            "# branch.oid a1b2c3d",
            "# branch.head main",
            "1 .M N... 100644 100644 100644 aaa bbb a file with spaces.txt",
            "? tara insir.txt");

        var snapshot = StatusParser.Parse(input);

        Assert.Contains(snapshot.Changes, c => c.Path == "a file with spaces.txt");
        Assert.Contains(snapshot.Changes, c => c.Path == "tara insir.txt");
    }

    [Fact]
    public void Parse_HandlesEmptyOutput()
    {
        var snapshot = StatusParser.Parse("");

        Assert.Empty(snapshot.Changes);
        Assert.False(snapshot.HasCommits);
    }

    [Fact]
    public async Task Parse_MatchesRealGitOutput()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("staged.txt", "a\n");
        repo.WriteFile("untracked.txt", "b\n");
        await repo.GitAsync("add", "--", "staged.txt");

        var result = await repo.GitAsync("status", "--porcelain=v2", "-z", "--branch");
        var snapshot = StatusParser.Parse(result.StdOut);

        Assert.Equal("main", snapshot.Branch);
        Assert.True(snapshot.HasCommits);
        Assert.True(snapshot.Changes.Single(c => c.Path == "staged.txt").IsStaged);
        Assert.True(snapshot.Changes.Single(c => c.Path == "untracked.txt").IsUntracked);
    }
}
