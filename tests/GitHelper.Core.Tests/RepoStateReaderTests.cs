using GitHelper.Core.Git;
using GitHelper.Core.Repo;

namespace GitHelper.Core.Tests;

public class RepoStateReaderTests
{
    private static RepoStateReader NewReader() => new(new GitRunner());

    [Fact]
    public async Task ReadAsync_ReadsBranchCommitsAndChanges()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("staged.txt", "a\n");
        repo.WriteFile("untracked.txt", "b\n");
        await repo.GitAsync("add", "--", "staged.txt");

        var state = await NewReader().ReadAsync(repo.Path);

        Assert.Equal("main", state.Branch);
        Assert.False(state.IsDetached);
        Assert.True(state.HasCommits);
        Assert.False(state.HasRemote);
        Assert.Null(state.Upstream);
        Assert.True(state.HasStagedChanges);
        Assert.Single(state.Staged);
        Assert.Single(state.Untracked);
        Assert.Single(state.RecentCommits);
        Assert.Single(state.Branches);
    }

    [Fact]
    public async Task ReadAsync_HandlesRepositoryWithNoCommits()
    {
        using var repo = await TestRepo.CreateAsync(withInitialCommit: false);

        var state = await NewReader().ReadAsync(repo.Path);

        Assert.False(state.HasCommits);
        Assert.False(state.CanUndoLastCommit);
        Assert.Empty(state.RecentCommits);
        Assert.Empty(state.Branches); // no branch ref exists until the first commit
    }

    [Fact]
    public async Task ReadAsync_ReportsCanUndoLastCommitOnlyWhenAParentExists()
    {
        using var repo = await TestRepo.CreateAsync();

        var afterFirst = await NewReader().ReadAsync(repo.Path);
        Assert.False(afterFirst.CanUndoLastCommit);

        repo.WriteFile("second.txt", "x\n");
        await repo.GitAsync("add", "-A");
        await repo.GitAsync("commit", "-q", "-m", "second");

        var afterSecond = await NewReader().ReadAsync(repo.Path);
        Assert.True(afterSecond.CanUndoLastCommit);
    }

    [Fact]
    public async Task ReadAsync_DetectsDetachedHead()
    {
        using var repo = await TestRepo.CreateAsync();
        var head = (await repo.GitAsync("rev-parse", "HEAD")).StdOut.Trim();
        await repo.GitAsync("checkout", "-q", head);

        var state = await NewReader().ReadAsync(repo.Path);

        Assert.True(state.IsDetached);
        Assert.Null(state.Branch);
    }

    [Fact]
    public async Task FindRepoRootAsync_ReturnsNullOutsideARepository()
    {
        var dir = Path.Combine(Path.GetTempPath(), "githelper-notarepo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Null(await NewReader().FindRepoRootAsync(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task FindRepoRootAsync_FindsRootFromASubdirectory()
    {
        using var repo = await TestRepo.CreateAsync();
        var sub = Path.Combine(repo.Path, "nested", "deeper");
        Directory.CreateDirectory(sub);

        var root = await NewReader().FindRepoRootAsync(sub);

        Assert.NotNull(root);
        // Temp paths may differ by symlink or casing; compare resolved leaf identity.
        Assert.Equal(
            Path.GetFileName(repo.Path),
            Path.GetFileName(root!.TrimEnd('/', '\\')));
    }
}
