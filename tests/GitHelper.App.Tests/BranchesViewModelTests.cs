using GitHelper.App.ViewModels;
using GitHelper.Core.Actions;
using GitHelper.Core.Content;
using GitHelper.Core.Git;
using GitHelper.Core.Model;
using GitHelper.Core.Repo;

namespace GitHelper.App.Tests;

public class BranchesViewModelTests
{
    private sealed record Fixture(
        BranchesViewModel Branches,
        ExplainPanelViewModel Panel,
        RepoStateReader Reader);

    private static Fixture NewFixture()
    {
        var runner = new GitRunner();
        var reader = new RepoStateReader(runner);
        var service = new ActionService(runner, reader, ContentLibrary.Load());
        var panel = new ExplainPanelViewModel(service, new StubConfirmationDialog(), new InMemorySettingsStore());
        return new Fixture(new BranchesViewModel(panel), panel, reader);
    }

    private static RepoState State(
        string? branch = "main",
        bool isDetached = false,
        string? upstream = null,
        int ahead = 0,
        int behind = 0,
        bool hasRemote = false,
        params BranchInfo[] branches)
        => new(
            RepoRoot: @"C:\r", Branch: branch, IsDetached: isDetached, Upstream: upstream,
            Ahead: ahead, Behind: behind, HasCommits: true, HasRemote: hasRemote,
            Changes: Array.Empty<FileChange>(),
            RecentCommits: Array.Empty<CommitInfo>(),
            Branches: branches.Length > 0 ? branches : new[] { new BranchInfo("main", upstream) });

    [Fact]
    public async Task Update_ListsBranchesAndMarksTheCurrentOne()
    {
        using var repo = await TestRepo.CreateAsync();
        await repo.GitAsync("branch", "feature");
        var f = NewFixture();

        f.Branches.Update(await f.Reader.ReadAsync(repo.Path));

        Assert.Equal(2, f.Branches.Branches.Count);
        Assert.True(f.Branches.Branches.Single(b => b.Name == "main").IsCurrent);
        Assert.False(f.Branches.Branches.Single(b => b.Name == "feature").IsCurrent);
    }

    [Fact]
    public void Update_ForbidsSwitchingToOrDeletingTheCurrentBranch()
    {
        var f = NewFixture();

        f.Branches.Update(State(branches: new[]
        {
            new BranchInfo("main", null),
            new BranchInfo("feature", null),
        }));

        var current = f.Branches.Branches.Single(b => b.Name == "main");
        var other = f.Branches.Branches.Single(b => b.Name == "feature");

        Assert.False(current.CanSwitch);
        Assert.False(current.CanDelete);
        Assert.True(other.CanSwitch);
        Assert.True(other.CanDelete);
    }

    [Fact]
    public void Update_DescribesAnUpstreamOrItsAbsenceInWords()
    {
        var f = NewFixture();

        f.Branches.Update(State(branches: new[]
        {
            new BranchInfo("main", "origin/main"),
            new BranchInfo("local-only", null),
        }));

        Assert.Equal("origin/main", f.Branches.Branches.Single(b => b.Name == "main").UpstreamLabel);
        Assert.Equal(
            "not on the server yet",
            f.Branches.Branches.Single(b => b.Name == "local-only").UpstreamLabel);
    }

    [Fact]
    public void Update_ShowsTheCurrentBranchName()
    {
        var f = NewFixture();

        f.Branches.Update(State(branch: "main"));

        Assert.False(f.Branches.IsDetached);
        Assert.Equal("main", f.Branches.CurrentBranchLabel);
    }

    [Fact]
    public void Update_ExplainsDetachedHeadInsteadOfShowingABlankBranch()
    {
        var f = NewFixture();

        f.Branches.Update(State(branch: null, isDetached: true));

        Assert.True(f.Branches.IsDetached);
        Assert.Contains("not on a branch", f.Branches.CurrentBranchLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0, 0, "in step with origin/main")]
    [InlineData(1, 0, "1 commit to send")]
    [InlineData(3, 0, "3 commits to send")]
    [InlineData(0, 1, "1 commit to get")]
    [InlineData(0, 2, "2 commits to get")]
    [InlineData(2, 3, "2 to send, 3 to get")]
    public void SyncSummary_PhrasesAheadAndBehindPlainly(int ahead, int behind, string expected)
    {
        var f = NewFixture();

        f.Branches.Update(State(upstream: "origin/main", ahead: ahead, behind: behind, hasRemote: true));

        Assert.Equal(expected, f.Branches.SyncSummary);
    }

    [Fact]
    public void SyncSummary_SaysSoWhenThereIsNoUpstream()
    {
        var f = NewFixture();

        f.Branches.Update(State(upstream: null, hasRemote: true));

        Assert.Contains("not linked", f.Branches.SyncSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Update_ReportsWhetherARemoteExistsAtAll()
    {
        var f = NewFixture();

        f.Branches.Update(State(hasRemote: false));
        Assert.False(f.Branches.HasRemote);

        f.Branches.Update(State(hasRemote: true));
        Assert.True(f.Branches.HasRemote);
    }

    [Fact]
    public async Task Update_ReplacesRowsRatherThanAccumulatingThem()
    {
        using var repo = await TestRepo.CreateAsync();
        var f = NewFixture();
        var state = await f.Reader.ReadAsync(repo.Path);

        f.Branches.Update(state);
        f.Branches.Update(state);

        Assert.Single(f.Branches.Branches);
    }

    [Fact]
    public async Task CreateBranchCommand_PreviewsAndThenCreatesOnConfirm()
    {
        using var repo = await TestRepo.CreateAsync();
        var f = NewFixture();
        f.Branches.Update(await f.Reader.ReadAsync(repo.Path));
        f.Branches.NewBranchName = "feature";

        // create-branch is Safe, so ShowAndRunIfUngated runs it straight away.
        await f.Branches.CreateBranchCommand.ExecuteAsync(null);

        var after = await f.Reader.ReadAsync(repo.Path);
        Assert.Equal("feature", after.Branch);
    }

    [Fact]
    public void OnActionCompleted_ClearsTheBranchNameBoxWhenThatBranchAppeared()
    {
        var f = NewFixture();
        f.Branches.NewBranchName = "feature";

        var before = State(branches: new[] { new BranchInfo("main", null) });
        var after = State(branches: new[]
        {
            new BranchInfo("main", null),
            new BranchInfo("feature", null),
        });

        f.Branches.OnActionCompleted(OutcomeBetween(before, after));

        Assert.Equal(string.Empty, f.Branches.NewBranchName);
    }

    [Fact]
    public void OnActionCompleted_KeepsTheTypedNameWhenAnUnrelatedActionSucceeded()
    {
        // Typing a branch name and then clicking Fetch or Pull must not silently
        // discard it — only an actual branch creation should clear the box.
        var f = NewFixture();
        f.Branches.NewBranchName = "feature";

        var state = State(branches: new[] { new BranchInfo("main", null) });

        f.Branches.OnActionCompleted(OutcomeBetween(state, state));

        Assert.Equal("feature", f.Branches.NewBranchName);
    }

    [Fact]
    public void OnActionCompleted_KeepsTheBranchNameWhenTheActionFailed()
    {
        var f = NewFixture();
        f.Branches.NewBranchName = "feature";

        var before = State(branches: new[] { new BranchInfo("main", null) });
        var after = State(branches: new[]
        {
            new BranchInfo("main", null),
            new BranchInfo("feature", null),
        });

        f.Branches.OnActionCompleted(OutcomeBetween(before, after) with { Success = false });

        Assert.Equal("feature", f.Branches.NewBranchName);
    }

    private static ActionOutcome OutcomeBetween(RepoState before, RepoState after)
        => new(
            Success: true,
            Result: new GitCommandResult(new[] { "switch", "-c", "feature" }, "", "", 0, TimeSpan.Zero),
            Narration: "created",
            Error: null,
            Before: before,
            After: after,
            Blockers: Array.Empty<PreconditionResult>());

    [Fact]
    public async Task SwitchCommand_PreviewsWithoutSwitchingBecauseSwitchingIsGated()
    {
        using var repo = await TestRepo.CreateAsync();
        await repo.GitAsync("branch", "feature");
        var f = NewFixture();
        f.Branches.Update(await f.Reader.ReadAsync(repo.Path));

        await f.Branches.Branches.Single(b => b.Name == "feature").SwitchCommand.ExecuteAsync(null);

        Assert.True(f.Panel.RequiresConfirmation);
        var after = await f.Reader.ReadAsync(repo.Path);
        Assert.Equal("main", after.Branch); // not switched yet
    }

    [Fact]
    public async Task SwitchCommand_ThenConfirming_ChangesBranch()
    {
        using var repo = await TestRepo.CreateAsync();
        await repo.GitAsync("branch", "feature");
        var f = NewFixture();
        f.Branches.Update(await f.Reader.ReadAsync(repo.Path));

        await f.Branches.Branches.Single(b => b.Name == "feature").SwitchCommand.ExecuteAsync(null);
        await f.Panel.RunAsync();

        var after = await f.Reader.ReadAsync(repo.Path);
        Assert.Equal("feature", after.Branch);
    }

    [Fact]
    public async Task DeleteCommand_ThenConfirming_RemovesAMergedBranch()
    {
        using var repo = await TestRepo.CreateAsync();
        await repo.GitAsync("branch", "feature");
        var f = NewFixture();
        f.Branches.Update(await f.Reader.ReadAsync(repo.Path));

        await f.Branches.Branches.Single(b => b.Name == "feature").DeleteCommand.ExecuteAsync(null);
        await f.Panel.RunAsync();

        var after = await f.Reader.ReadAsync(repo.Path);
        Assert.DoesNotContain(after.Branches, b => b.Name == "feature");
    }

    [Fact]
    public async Task PushCommand_IsBlockedWithATranslatedMessageInDetachedHead()
    {
        using var repo = await TestRepo.CreateAsync();
        await repo.GitAsync("remote", "add", "origin", "https://example.invalid/nope.git");
        var head = (await repo.GitAsync("rev-parse", "HEAD")).StdOut.Trim();
        await repo.GitAsync("checkout", "-q", head);
        var f = NewFixture();
        f.Branches.Update(await f.Reader.ReadAsync(repo.Path));

        await f.Branches.PushCommand.ExecuteAsync(null);

        // The engine's RequiresNotDetached blocks it; no crash, and a readable reason.
        Assert.False(f.Panel.CanRun);
        Assert.Contains(f.Panel.Blockers, b => b.Contains("detached", StringComparison.OrdinalIgnoreCase));
    }
}
