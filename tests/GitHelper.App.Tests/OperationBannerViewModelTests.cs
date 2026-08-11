using GitHelper.App.Settings;
using GitHelper.App.ViewModels;
using GitHelper.Core.Actions;
using GitHelper.Core.Content;
using GitHelper.Core.Git;
using GitHelper.Core.Model;
using GitHelper.Core.Repo;

namespace GitHelper.App.Tests;

/// <summary>
/// The band that says a merge is part-way through. It renders from RepoState rather than
/// from an outcome, so a merge started elsewhere — or one still running from a previous
/// run of the app — shows up the same as one started here.
/// </summary>
public class OperationBannerViewModelTests
{
    private static OperationBannerViewModel NewBanner()
    {
        var runner = new GitRunner();
        var service = new ActionService(runner, new RepoStateReader(runner), ContentLibrary.Load());
        var panel = new ExplainPanelViewModel(
            service, new StubConfirmationDialog(), new InMemorySettingsStore());

        return new OperationBannerViewModel(panel);
    }

    private static FileChange Conflicted(string path)
        => new(path, null, ChangeKind.Unmerged, ChangeKind.Unmerged);

    private static RepoState State(OperationState? operation, params FileChange[] changes)
        => new(
            RepoRoot: @"C:\repos\demo",
            Branch: "main",
            IsDetached: false,
            Upstream: null,
            Ahead: 0,
            Behind: 0,
            HasCommits: true,
            HasRemote: false,
            Changes: changes,
            RecentCommits: Array.Empty<CommitInfo>(),
            Branches: Array.Empty<BranchInfo>(),
            Tags: Array.Empty<TagInfo>(),
            Stashes: Array.Empty<StashInfo>(),
            Operation: operation);

    [Fact]
    public void IsHiddenWhenNothingIsInFlight()
    {
        var banner = NewBanner();

        banner.Update(State(null));

        Assert.False(banner.IsVisible);
    }

    [Fact]
    public void AppearsDuringAMergeAndNamesBothBranches()
    {
        var banner = NewBanner();

        banner.Update(State(new OperationState(OperationKind.Merge, "feature"), Conflicted("a.txt")));

        Assert.True(banner.IsVisible);
        Assert.Contains("feature", banner.Headline);
        Assert.Contains("main", banner.Headline);
    }

    [Fact]
    public void SaysHowManyFilesStillNeedFixing()
    {
        var banner = NewBanner();

        banner.Update(State(
            new OperationState(OperationKind.Merge, "feature"),
            Conflicted("a.txt"),
            Conflicted("b.txt")));

        Assert.Contains("2", banner.Detail);
    }

    [Fact]
    public void CannotFinishWhileAFileStillConflicts()
    {
        var banner = NewBanner();

        banner.Update(State(new OperationState(OperationKind.Merge, "feature"), Conflicted("a.txt")));

        Assert.False(banner.CanFinish);
    }

    [Fact]
    public void CanFinishOnceEveryConflictIsMarkedFixed()
    {
        var banner = NewBanner();

        banner.Update(State(
            new OperationState(OperationKind.Merge, "feature"),
            new FileChange("a.txt", null, ChangeKind.Modified, ChangeKind.None)));

        Assert.True(banner.CanFinish);
        Assert.Contains("finish", banner.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SaysAnotherBranchWhenTheIncomingCommitHasNoName()
    {
        var banner = NewBanner();

        banner.Update(State(new OperationState(OperationKind.Merge, null), Conflicted("a.txt")));

        Assert.Contains("another branch", banner.Headline);
    }

    [Fact]
    public void GoesAwayAgainOnceTheMergeEnds()
    {
        var banner = NewBanner();

        banner.Update(State(new OperationState(OperationKind.Merge, "feature"), Conflicted("a.txt")));
        banner.Update(State(null));

        Assert.False(banner.IsVisible);
    }

    [Fact]
    public async Task AbandonExplainsItselfBeforeItRuns()
    {
        // The band is not a shortcut past the confirmation gate. merge-abort is Caution, so
        // clicking it explains what will happen and waits — throwing away conflicts already
        // fixed by hand is not something to do on one click.
        using var repo = await TestRepo.CreateAsync();
        await repo.StartConflictingMergeAsync();

        var runner = new GitRunner();
        var reader = new RepoStateReader(runner);
        var panel = new ExplainPanelViewModel(
            new ActionService(runner, reader, ContentLibrary.Load()),
            new StubConfirmationDialog(),
            new InMemorySettingsStore());
        var banner = new OperationBannerViewModel(panel);

        banner.Update(await reader.ReadAsync(repo.Path));
        Assert.True(banner.IsVisible);
        Assert.False(banner.CanFinish);

        await banner.AbandonCommand.ExecuteAsync(null);

        Assert.Equal("Abandon the merge", panel.Title);
        Assert.True(panel.RequiresInlineConfirmation);
        Assert.NotNull((await reader.ReadAsync(repo.Path)).Operation);

        await panel.RunAsync();

        Assert.Null((await reader.ReadAsync(repo.Path)).Operation);
    }

    [Fact]
    public async Task FinishRunsOnceEveryConflictIsFixed()
    {
        using var repo = await TestRepo.CreateAsync();
        await repo.StartConflictingMergeAsync();
        repo.WriteFile("conflict.txt", "reconciled by hand\n");
        await repo.GitAsync("add", "--", "conflict.txt");

        var runner = new GitRunner();
        var reader = new RepoStateReader(runner);
        var panel = new ExplainPanelViewModel(
            new ActionService(runner, reader, ContentLibrary.Load()),
            new StubConfirmationDialog(),
            new InMemorySettingsStore());
        var banner = new OperationBannerViewModel(panel);

        banner.Update(await reader.ReadAsync(repo.Path));
        Assert.True(banner.CanFinish);

        await banner.FinishCommand.ExecuteAsync(null);
        await panel.RunAsync();

        var finished = await reader.ReadAsync(repo.Path);
        Assert.Null(finished.Operation);
        Assert.Contains(finished.RecentCommits, c => c.Subject.Contains("Merge"));
    }
}
