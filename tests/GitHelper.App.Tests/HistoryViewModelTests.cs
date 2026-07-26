using GitHelper.App.ViewModels;
using GitHelper.Core.Actions;
using GitHelper.Core.Content;
using GitHelper.Core.Git;
using GitHelper.Core.Repo;

namespace GitHelper.App.Tests;

public class HistoryViewModelTests
{
    private sealed record Fixture(
        HistoryViewModel History,
        ExplainPanelViewModel Panel,
        RepoStateReader Reader);

    private static Fixture NewFixture()
    {
        var runner = new GitRunner();
        var reader = new RepoStateReader(runner);
        var service = new ActionService(runner, reader, ContentLibrary.Load());
        var panel = new ExplainPanelViewModel(service, new StubConfirmationDialog(), new InMemorySettingsStore());
        return new Fixture(new HistoryViewModel(panel), panel, reader);
    }

    [Fact]
    public async Task Update_ListsCommitsNewestFirstWithTheirDetails()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        await repo.GitAsync("add", "-A");
        await repo.GitAsync("commit", "-q", "-m", "second commit");
        var f = NewFixture();

        f.History.Update(await f.Reader.ReadAsync(repo.Path));

        Assert.Equal(2, f.History.Commits.Count);
        Assert.Equal("second commit", f.History.Commits[0].Subject);
        Assert.Equal("initial", f.History.Commits[1].Subject);
        Assert.Equal("Test User", f.History.Commits[0].Author);
        Assert.NotEmpty(f.History.Commits[0].ShortHash);
        Assert.True(f.History.HasCommits);
    }

    [Fact]
    public async Task Update_ReportsAnEmptyHistoryInAFreshRepository()
    {
        using var repo = await TestRepo.CreateAsync(withInitialCommit: false);
        var f = NewFixture();

        f.History.Update(await f.Reader.ReadAsync(repo.Path));

        Assert.Empty(f.History.Commits);
        Assert.False(f.History.HasCommits);
    }

    [Fact]
    public async Task Update_OffersUndoOnTheNewestCommitOnly()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        await repo.GitAsync("add", "-A");
        await repo.GitAsync("commit", "-q", "-m", "second");
        var f = NewFixture();

        f.History.Update(await f.Reader.ReadAsync(repo.Path));

        Assert.True(f.History.Commits[0].CanUndo);
        Assert.False(f.History.Commits[1].CanUndo);
    }

    [Fact]
    public async Task Update_RefusesUndoOnTheVeryFirstCommit()
    {
        using var repo = await TestRepo.CreateAsync();
        var f = NewFixture();

        f.History.Update(await f.Reader.ReadAsync(repo.Path));

        // The engine's RequiresParentCommit already refuses this; the row must agree.
        Assert.Single(f.History.Commits);
        Assert.False(f.History.Commits[0].CanUndo);
    }

    [Fact]
    public async Task Update_ReplacesRowsRatherThanAccumulatingThem()
    {
        using var repo = await TestRepo.CreateAsync();
        var f = NewFixture();
        var state = await f.Reader.ReadAsync(repo.Path);

        f.History.Update(state);
        f.History.Update(state);

        Assert.Single(f.History.Commits);
    }

    [Fact]
    public async Task UndoCommand_PreviewsWithoutUndoingBecauseUndoIsGated()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        await repo.GitAsync("add", "-A");
        await repo.GitAsync("commit", "-q", "-m", "second");
        var f = NewFixture();
        f.History.Update(await f.Reader.ReadAsync(repo.Path));

        await f.History.Commits[0].UndoCommand.ExecuteAsync(null);

        Assert.Equal(ExplainPanelState.Explaining, f.Panel.PanelState);
        Assert.True(f.Panel.RequiresConfirmation);
        var after = await f.Reader.ReadAsync(repo.Path);
        Assert.Equal(2, after.RecentCommits.Count); // nothing undone yet
    }

    [Fact]
    public async Task UndoCommand_ThenConfirming_RemovesTheCommitButKeepsTheWork()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        await repo.GitAsync("add", "-A");
        await repo.GitAsync("commit", "-q", "-m", "second");
        var f = NewFixture();
        f.History.Update(await f.Reader.ReadAsync(repo.Path));

        await f.History.Commits[0].UndoCommand.ExecuteAsync(null);
        await f.Panel.RunAsync();

        var after = await f.Reader.ReadAsync(repo.Path);
        Assert.Single(after.RecentCommits);
        Assert.Single(after.Staged); // --soft keeps the work, staged
    }

    [Fact]
    public async Task RelativeDate_UsesTheInjectedClockSoItIsNotFlaky()
    {
        using var repo = await TestRepo.CreateAsync();
        var f = NewFixture();
        var state = await f.Reader.ReadAsync(repo.Path);
        // Pretend it is two hours after the commit was authored.
        f.History.Now = () => state.RecentCommits[0].Date.AddHours(2);

        f.History.Update(state);

        Assert.Equal("2 hours ago", f.History.Commits[0].RelativeDate);
    }
}
