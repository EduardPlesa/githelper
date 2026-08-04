using GitHelper.App.ViewModels;
using GitHelper.Core.Actions;
using GitHelper.Core.Content;
using GitHelper.Core.Git;
using GitHelper.Core.Model;
using GitHelper.Core.Repo;

namespace GitHelper.App.Tests;

public class ChangesViewModelTests
{
    private sealed record Fixture(
        ChangesViewModel Changes,
        ExplainPanelViewModel Panel,
        StubConfirmationDialog Confirmations,
        RepoStateReader Reader);

    private static Fixture NewFixture()
    {
        var runner = new GitRunner();
        var reader = new RepoStateReader(runner);
        var service = new ActionService(runner, reader, ContentLibrary.Load());
        var confirmations = new StubConfirmationDialog();
        var panel = new ExplainPanelViewModel(service, confirmations, new InMemorySettingsStore());
        return new Fixture(new ChangesViewModel(panel), panel, confirmations, reader);
    }

    [Fact]
    public async Task Update_SplitsStagedFromNotStaged()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("staged.txt", "a\n");
        repo.WriteFile("untracked.txt", "b\n");
        await repo.GitAsync("add", "--", "staged.txt");
        var f = NewFixture();

        f.Changes.Update(await f.Reader.ReadAsync(repo.Path), null);

        Assert.Equal(new[] { "staged.txt" }, f.Changes.Staged.Select(r => r.Path));
        Assert.Equal(new[] { "untracked.txt" }, f.Changes.Unstaged.Select(r => r.Path));
        Assert.True(f.Changes.HasStagedChanges);
        Assert.True(f.Changes.HasAnyChanges);
    }

    [Fact]
    public async Task Update_ShowsAFileThatIsBothStagedAndFurtherModifiedInBothLists()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "first\n");
        await repo.GitAsync("add", "--", "a.txt");
        repo.WriteFile("a.txt", "edited again\n");
        var f = NewFixture();

        f.Changes.Update(await f.Reader.ReadAsync(repo.Path), null);

        Assert.Contains("a.txt", f.Changes.Staged.Select(r => r.Path));
        Assert.Contains("a.txt", f.Changes.Unstaged.Select(r => r.Path));
    }

    [Fact]
    public async Task Update_ReportsNoChangesInACleanRepository()
    {
        using var repo = await TestRepo.CreateAsync();
        var f = NewFixture();

        f.Changes.Update(await f.Reader.ReadAsync(repo.Path), null);

        Assert.Empty(f.Changes.Staged);
        Assert.Empty(f.Changes.Unstaged);
        Assert.False(f.Changes.HasAnyChanges);
        Assert.False(f.Changes.HasStagedChanges);
    }

    [Fact]
    public async Task Update_ReplacesRowsRatherThanAccumulatingThem()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        var f = NewFixture();
        var state = await f.Reader.ReadAsync(repo.Path);

        f.Changes.Update(state, null);
        f.Changes.Update(state, null);

        Assert.Single(f.Changes.Unstaged);
    }

    [Theory]
    [InlineData(ChangeKind.Modified, "modified")]
    [InlineData(ChangeKind.Added, "new file")]
    [InlineData(ChangeKind.Deleted, "deleted")]
    [InlineData(ChangeKind.Renamed, "renamed")]
    [InlineData(ChangeKind.Untracked, "new file")]
    [InlineData(ChangeKind.Unmerged, "conflicted")]
    public void StatusLabel_UsesPlainEnglishNotGitLetters(ChangeKind kind, string expected)
    {
        var change = new FileChange("a.txt", null, ChangeKind.None, kind);

        var row = new FileChangeRowViewModel(change, staged: false, (_, _) => Task.CompletedTask);

        Assert.Equal(expected, row.StatusLabel);
    }

    [Fact]
    public void StatusLabel_ForAStagedRowDescribesTheIndexSideNotTheWorktreeSide()
    {
        var change = new FileChange("a.txt", null, ChangeKind.Added, ChangeKind.Modified);

        var staged = new FileChangeRowViewModel(change, staged: true, (_, _) => Task.CompletedTask);
        var unstaged = new FileChangeRowViewModel(change, staged: false, (_, _) => Task.CompletedTask);

        Assert.Equal("new file", staged.StatusLabel);
        Assert.Equal("modified", unstaged.StatusLabel);
    }

    [Fact]
    public async Task StageCommand_RunsImmediatelyBecauseStagingIsSafe()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        var f = NewFixture();
        f.Changes.Update(await f.Reader.ReadAsync(repo.Path), null);

        await f.Changes.Unstaged.Single().StageCommand.ExecuteAsync(null);

        var after = await f.Reader.ReadAsync(repo.Path);
        Assert.Single(after.Staged);
    }

    [Fact]
    public async Task UnstageCommand_RunsImmediately()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        await repo.GitAsync("add", "-A");
        var f = NewFixture();
        f.Changes.Update(await f.Reader.ReadAsync(repo.Path), null);

        await f.Changes.Staged.Single().UnstageCommand.ExecuteAsync(null);

        var after = await f.Reader.ReadAsync(repo.Path);
        Assert.Empty(after.Staged);
    }

    [Fact]
    public async Task DiscardCommand_GoesThroughTheDestructiveModal()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("README.md", "vandalised\n");
        var f = NewFixture();
        f.Confirmations.NextAnswer = false;
        f.Changes.Update(await f.Reader.ReadAsync(repo.Path), null);

        await f.Changes.Unstaged.Single().DiscardCommand.ExecuteAsync(null);

        Assert.Equal(1, f.Confirmations.CallCount);
        // Declined, so the edit survives.
        Assert.Equal(
            "vandalised\n",
            File.ReadAllText(Path.Combine(repo.Path, "README.md")).Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task StageAllCommand_StagesEverythingIncludingUntrackedFiles()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        repo.WriteFile("b.txt", "y\n");
        var f = NewFixture();
        f.Changes.Update(await f.Reader.ReadAsync(repo.Path), null);

        await f.Changes.StageAllCommand.ExecuteAsync(null);

        var after = await f.Reader.ReadAsync(repo.Path);
        Assert.Equal(2, after.Staged.Count);
    }

    [Fact]
    public async Task CommitCommand_PreviewsWithoutCommittingBecauseCommitIsGated()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        await repo.GitAsync("add", "-A");
        var f = NewFixture();
        f.Changes.Update(await f.Reader.ReadAsync(repo.Path), null);
        f.Changes.CommitMessage = "add a file";

        await f.Changes.CommitCommand.ExecuteAsync(null);

        Assert.Equal(ExplainPanelState.Explaining, f.Panel.PanelState);
        Assert.True(f.Panel.RequiresConfirmation);
        var after = await f.Reader.ReadAsync(repo.Path);
        Assert.Single(after.RecentCommits); // nothing committed yet
    }

    [Fact]
    public async Task CommitCommand_ThenConfirming_CreatesTheCommit()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        await repo.GitAsync("add", "-A");
        var f = NewFixture();
        f.Changes.Update(await f.Reader.ReadAsync(repo.Path), null);
        f.Changes.CommitMessage = "add a file";

        await f.Changes.CommitCommand.ExecuteAsync(null);
        await f.Panel.RunAsync();

        var after = await f.Reader.ReadAsync(repo.Path);
        Assert.Equal("add a file", after.RecentCommits[0].Subject);
    }

    [Fact]
    public void OnActionCompleted_ClearsTheCommitBoxOnlyWhenACommitActuallyAppeared()
    {
        var f = NewFixture();
        f.Changes.CommitMessage = "typed but not committed";

        f.Changes.OnActionCompleted(OutcomeWithCommitCounts(before: 1, after: 1));
        Assert.Equal("typed but not committed", f.Changes.CommitMessage);

        f.Changes.OnActionCompleted(OutcomeWithCommitCounts(before: 1, after: 2));
        Assert.Equal(string.Empty, f.Changes.CommitMessage);
    }

    [Fact]
    public void OnActionCompleted_KeepsTheMessageWhenTheCommitFailed()
    {
        var f = NewFixture();
        f.Changes.CommitMessage = "keep me";

        var failed = OutcomeWithCommitCounts(before: 1, after: 2) with { Success = false };
        f.Changes.OnActionCompleted(failed);

        Assert.Equal("keep me", f.Changes.CommitMessage);
    }

    private static ActionOutcome OutcomeWithCommitCounts(int before, int after)
    {
        var result = new GitCommandResult(new[] { "commit" }, "", "", 0, TimeSpan.Zero);
        return new ActionOutcome(
            Success: true,
            Result: result,
            Narration: "done",
            Error: null,
            Before: StateWithCommits(before),
            After: StateWithCommits(after),
            Blockers: Array.Empty<PreconditionResult>());

        static RepoState StateWithCommits(int count) => new(
            RepoRoot: @"C:\r", Branch: "main", IsDetached: false, Upstream: null,
            Ahead: 0, Behind: 0, HasCommits: count > 0, HasRemote: false,
            Changes: Array.Empty<FileChange>(),
            RecentCommits: Enumerable.Range(0, count)
                .Select(i => new CommitInfo($"h{i}", $"h{i}", "A", DateTimeOffset.UnixEpoch, $"c{i}"))
                .ToArray(),
            Branches: Array.Empty<BranchInfo>(),
            Tags: Array.Empty<TagInfo>(),
            Stashes: Array.Empty<StashInfo>());
    }
}
