using GitHelper.Core.Actions;
using GitHelper.Core.Git;
using GitHelper.Core.Model;
using GitHelper.Core.Repo;

namespace GitHelper.Core.Tests;

public class ActionCatalogTests
{
    private static readonly GitRunner Runner = new();
    private static readonly RepoStateReader Reader = new(Runner);

    /// <summary>Reads state, builds the action's argv, runs it, and returns the resulting state.</summary>
    private static async Task<RepoState> RunActionAsync(TestRepo repo, ActionRequest request)
    {
        var action = ActionCatalog.Find(request.ActionId)!;
        var before = await Reader.ReadAsync(repo.Path);
        var args = action.BuildArgs(before, request);

        var result = await Runner.RunAsync(repo.Path, args);
        Assert.True(result.Success, $"{result.CommandLine} failed: {result.StdErr}");

        return await Reader.ReadAsync(repo.Path);
    }

    [Fact]
    public void All_ContainsExactlyTheThirteenV1Actions()
    {
        var expected = new[]
        {
            "stage-file", "unstage-file", "stage-all", "unstage-all", "commit",
            "create-branch", "switch-branch", "fetch", "pull", "push",
            "discard-file", "undo-last-commit", "delete-branch",
        };

        Assert.Equal(expected.OrderBy(x => x), ActionCatalog.All.Select(a => a.Id).OrderBy(x => x));
    }

    [Fact]
    public void DiscardFile_IsTheOnlyDestructiveActionInV1()
    {
        var destructive = ActionCatalog.All.Where(a => a.Danger == Danger.Destructive).Select(a => a.Id);

        Assert.Equal(new[] { "discard-file" }, destructive);
    }

    [Fact]
    public void EveryUndoActionIdRefersToARealAction()
    {
        foreach (var action in ActionCatalog.All.Where(a => a.UndoActionId is not null))
            Assert.NotNull(ActionCatalog.Find(action.UndoActionId!));
    }

    [Fact]
    public void Find_IsCaseInsensitiveAndReturnsNullForUnknownIds()
    {
        Assert.NotNull(ActionCatalog.Find("STAGE-FILE"));
        Assert.Null(ActionCatalog.Find("no-such-action"));
    }

    [Fact]
    public void EveryPathTakingActionPassesDoubleDashBeforeThePath()
    {
        var state = new RepoState(
            @"C:\r", "main", false, "origin/main", 0, 0, true, true,
            Array.Empty<FileChange>(), Array.Empty<CommitInfo>(), Array.Empty<BranchInfo>());

        foreach (var id in new[] { "stage-file", "unstage-file", "discard-file" })
        {
            var args = ActionCatalog.Find(id)!.BuildArgs(state, new ActionRequest(id, Path: "weird-name"));

            var separator = args.ToList().IndexOf("--");
            Assert.True(separator >= 0, $"{id} does not pass --");
            Assert.Equal("weird-name", args[separator + 1]);
        }
    }

    [Fact]
    public void Pull_RefusesToCreateAMergeCommit()
    {
        var state = new RepoState(
            @"C:\r", "main", false, "origin/main", 0, 1, true, true,
            Array.Empty<FileChange>(), Array.Empty<CommitInfo>(), Array.Empty<BranchInfo>());

        var args = ActionCatalog.Find("pull")!.BuildArgs(state, new ActionRequest("pull"));

        Assert.Contains("--ff-only", args);
    }

    [Fact]
    public void DeleteBranch_NeverForceDeletes()
    {
        var state = new RepoState(
            @"C:\r", "main", false, null, 0, 0, true, false,
            Array.Empty<FileChange>(), Array.Empty<CommitInfo>(), Array.Empty<BranchInfo>());

        var args = ActionCatalog.Find("delete-branch")!
            .BuildArgs(state, new ActionRequest("delete-branch", BranchName: "feature"));

        Assert.Contains("-d", args);
        Assert.DoesNotContain("-D", args);
    }

    [Fact]
    public void Push_SetsUpstreamOnlyWhenThereIsNone()
    {
        var withUpstream = new RepoState(
            @"C:\r", "main", false, "origin/main", 1, 0, true, true,
            Array.Empty<FileChange>(), Array.Empty<CommitInfo>(), Array.Empty<BranchInfo>());
        var withoutUpstream = withUpstream with { Upstream = null };

        Assert.DoesNotContain("--set-upstream",
            ActionCatalog.Find("push")!.BuildArgs(withUpstream, new ActionRequest("push")));

        var args = ActionCatalog.Find("push")!.BuildArgs(withoutUpstream, new ActionRequest("push"));
        Assert.Contains("--set-upstream", args);
        Assert.Contains("main", args);
    }

    [Fact]
    public async Task StageFile_ThenUnstageFile_RoundTrips()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");

        var staged = await RunActionAsync(repo, new ActionRequest("stage-file", Path: "a.txt"));
        Assert.Single(staged.Staged);

        var unstaged = await RunActionAsync(repo, new ActionRequest("unstage-file", Path: "a.txt"));
        Assert.Empty(unstaged.Staged);
    }

    [Fact]
    public async Task UnstageAll_WorksInARepositoryWithNoCommits()
    {
        // git restore --staged has no HEAD to restore from here; the descriptor must fall back.
        using var repo = await TestRepo.CreateAsync(withInitialCommit: false);
        repo.WriteFile("a.txt", "x\n");
        await repo.GitAsync("add", "-A");

        var state = await RunActionAsync(repo, new ActionRequest("unstage-all"));

        Assert.Empty(state.Staged);
        Assert.Single(state.Untracked);
    }

    [Fact]
    public async Task Commit_CreatesACommitWithTheGivenMessage()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        await repo.GitAsync("add", "-A");

        var state = await RunActionAsync(repo, new ActionRequest("commit", Message: "add a file"));

        Assert.Equal("add a file", state.RecentCommits[0].Subject);
        Assert.Empty(state.Staged);
    }

    [Fact]
    public async Task CreateBranch_SwitchesToTheNewBranch()
    {
        using var repo = await TestRepo.CreateAsync();

        var state = await RunActionAsync(repo, new ActionRequest("create-branch", BranchName: "feature"));

        Assert.Equal("feature", state.Branch);
    }

    [Fact]
    public async Task SwitchBranch_MovesBetweenExistingBranches()
    {
        using var repo = await TestRepo.CreateAsync();
        await repo.GitAsync("branch", "feature");

        var state = await RunActionAsync(repo, new ActionRequest("switch-branch", BranchName: "feature"));

        Assert.Equal("feature", state.Branch);
    }

    [Fact]
    public async Task DiscardFile_RestoresTheFileContents()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("README.md", "vandalised\n");

        var state = await RunActionAsync(repo, new ActionRequest("discard-file", Path: "README.md"));

        Assert.Empty(state.Unstaged);
        Assert.Equal("hello\n", File.ReadAllText(Path.Combine(repo.Path, "README.md")).Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task UndoLastCommit_RemovesTheCommitButKeepsTheChangesStaged()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        await repo.GitAsync("add", "-A");
        await repo.GitAsync("commit", "-q", "-m", "second");

        var state = await RunActionAsync(repo, new ActionRequest("undo-last-commit"));

        Assert.Single(state.RecentCommits);
        Assert.Equal("initial", state.RecentCommits[0].Subject);
        // --soft: the work is preserved, still staged.
        Assert.Single(state.Staged);
    }

    [Fact]
    public async Task DeleteBranch_RemovesAMergedBranch()
    {
        using var repo = await TestRepo.CreateAsync();
        await repo.GitAsync("branch", "feature");

        var state = await RunActionAsync(repo, new ActionRequest("delete-branch", BranchName: "feature"));

        Assert.DoesNotContain(state.Branches, b => b.Name == "feature");
    }
}
