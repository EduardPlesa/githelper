using GitHelper.Core.Git;
using GitHelper.Core.Model;
using GitHelper.Core.Repo;
using GitHelper.TestSupport;

namespace GitHelper.Core.Tests;

public class DiffReaderTests
{
    private static DiffReader Reader() => new(new GitRunner());

    [Fact]
    public async Task ReadsAnUnstagedChange()
    {
        using var repo = await TestRepo.CreateAsync();
        var path = await repo.AddUnstagedChangeAsync();

        var diff = await Reader().ReadAsync(repo.Path, path, DiffSide.Unstaged);

        Assert.Equal(DiffKind.Text, diff.Kind);
        Assert.Contains(
            diff.Hunks.SelectMany(h => h.Lines),
            l => l.Kind == DiffLineKind.Added && l.Text == "TWO");
    }

    [Fact]
    public async Task ReadsAStagedChange()
    {
        using var repo = await TestRepo.CreateAsync();
        var path = await repo.AddStagedAndFurtherModifiedAsync();

        var diff = await Reader().ReadAsync(repo.Path, path, DiffSide.Staged);

        Assert.Contains(
            diff.Hunks.SelectMany(h => h.Lines),
            l => l.Kind == DiffLineKind.Added && l.Text == "STAGED");
    }

    /// <summary>
    /// One path, two answers. The staged diff is index against HEAD and the unstaged one is
    /// worktree against index, so a file staged and then edited again must not report the
    /// same thing twice.
    /// </summary>
    [Fact]
    public async Task GivesTwoDifferentDiffsForOnePathWhenStagedAndFurtherModified()
    {
        using var repo = await TestRepo.CreateAsync();
        var path = await repo.AddStagedAndFurtherModifiedAsync();
        var reader = Reader();

        var staged = await reader.ReadAsync(repo.Path, path, DiffSide.Staged);
        var unstaged = await reader.ReadAsync(repo.Path, path, DiffSide.Unstaged);

        var stagedLines = staged.Hunks.SelectMany(h => h.Lines).ToList();
        var unstagedLines = unstaged.Hunks.SelectMany(h => h.Lines).ToList();

        // Index against HEAD: the staged edit is what arrived.
        Assert.Contains(stagedLines, l => l.Kind == DiffLineKind.Added && l.Text == "STAGED");

        // Worktree against index: the later edit is what arrived, and the staged text is
        // what it replaced — so STAGED appears here as Removed, never as Added.
        Assert.Contains(unstagedLines, l => l.Kind == DiffLineKind.Added && l.Text == "WORKTREE");
        Assert.Contains(unstagedLines, l => l.Kind == DiffLineKind.Removed && l.Text == "STAGED");

        // The staged diff knows nothing of the edit made after staging.
        Assert.DoesNotContain(stagedLines, l => l.Text == "WORKTREE");
    }

    /// <summary>
    /// git diff --no-index exits 1 whenever the files differ, which is every untracked file.
    /// Judging this read by Success would report every new file as a failure — the same trap
    /// ActionOutcome.Paused hit at rebase.
    /// </summary>
    [Fact]
    public async Task ReadsAnUntrackedFileAsAllAddedDespiteANonZeroExit()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("new.txt", "alpha\nbeta\n");

        var diff = await Reader().ReadAsync(repo.Path, "new.txt", DiffSide.Untracked);

        Assert.Equal(DiffKind.Text, diff.Kind);
        var hunk = Assert.Single(diff.Hunks);
        Assert.Equal(0, hunk.OldStart);
        Assert.All(hunk.Lines, l => Assert.Equal(DiffLineKind.Added, l.Kind));
        Assert.Equal(new[] { "alpha", "beta" }, hunk.Lines.Select(l => l.Text));
    }

    [Fact]
    public async Task ReportsAFileWithNoChangesAsEmpty()
    {
        using var repo = await TestRepo.CreateAsync();

        var diff = await Reader().ReadAsync(repo.Path, "README.md", DiffSide.Unstaged);

        Assert.Equal(DiffKind.Empty, diff.Kind);
    }

    /// <summary>
    /// A read that genuinely failed — no output and a non-zero exit — must not be reported as
    /// an empty diff, which would claim the file is unchanged. A file that has gone from disk
    /// is the ordinary way to reach this: --no-index has nothing to open.
    /// </summary>
    [Fact]
    public async Task ThrowsWhenTheFileIsNotThereToRead()
    {
        using var repo = await TestRepo.CreateAsync();

        await Assert.ThrowsAsync<GitReadException>(
            () => Reader().ReadAsync(repo.Path, "never-existed.txt", DiffSide.Untracked));
    }
}
