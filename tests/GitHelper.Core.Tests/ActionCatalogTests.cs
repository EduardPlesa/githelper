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
    public void All_ContainsExactlyTheTwentyNineActions()
    {
        var expected = new[]
        {
            "stage-file", "unstage-file", "stage-all", "unstage-all", "commit",
            "create-branch", "switch-branch", "fetch", "pull", "push",
            "discard-file", "undo-last-commit", "delete-branch",
            "connect-remote", "disconnect-remote",
            "create-tag", "delete-tag",
            "stash", "stash-pop", "stash-apply", "stash-drop",
            "rebase", "rebase-continue", "rebase-skip", "rebase-abort",
            "merge", "mark-resolved", "merge-continue", "merge-abort",
        };

        Assert.Equal(expected.OrderBy(x => x), ActionCatalog.All.Select(a => a.Id).OrderBy(x => x));
    }

    [Fact]
    public void Merge_BuildsMergeWithTheBranchAndNoEditor()
    {
        var args = ActionCatalog.Find("merge")!
            .BuildArgs(MinimalState(), new ActionRequest("merge", BranchName: "feature"));

        Assert.Equal(new[] { "merge", "--no-edit", "feature" }, args);
    }

    [Fact]
    public void MarkResolved_BuildsAddForTheOneFile()
    {
        var args = ActionCatalog.Find("mark-resolved")!
            .BuildArgs(MinimalState(), new ActionRequest("mark-resolved", Path: "conflict.txt"));

        Assert.Equal(new[] { "add", "--", "conflict.txt" }, args);
    }

    [Fact]
    public void MergeContinue_BuildsMergeContinue()
    {
        var args = ActionCatalog.Find("merge-continue")!
            .BuildArgs(MinimalState(), new ActionRequest("merge-continue"));

        Assert.Equal(new[] { "merge", "--continue" }, args);
    }

    [Fact]
    public void MergeAbort_BuildsMergeAbort()
    {
        var args = ActionCatalog.Find("merge-abort")!
            .BuildArgs(MinimalState(), new ActionRequest("merge-abort"));

        Assert.Equal(new[] { "merge", "--abort" }, args);
    }

    [Fact]
    public void Commit_IsBlockedWhileAMergeIsInFlight()
    {
        // `git commit` mid-merge finalises the merge. Without this guard the Commit button
        // would quietly end a merge, with a message written for something else entirely.
        // Staged changes and a message, so nothing else has grounds to object: the merge
        // must be the only reason this is refused.
        var ready = MinimalState() with
        {
            Changes = new[] { new FileChange("a.txt", null, ChangeKind.Modified, ChangeKind.None) },
        };
        var request = new ActionRequest("commit", Message: "hello");

        Assert.All(
            ActionCatalog.Find("commit")!.Preconditions.Select(p => p.Evaluate(ready, request)),
            r => Assert.True(r.Satisfied, r.Message));

        var merging = ready with
        {
            Operation = new OperationState(OperationKind.Merge, "feature"),
        };

        Assert.Contains(
            ActionCatalog.Find("commit")!.Preconditions.Select(p => p.Evaluate(merging, request)),
            r => !r.Satisfied);
    }

    [Fact]
    public async Task Merge_Stops_ThenResolves_ThenFinishes_AgainstARealRepository()
    {
        using var repo = await TestRepo.CreateAsync();
        await repo.StartConflictingMergeAsync();

        var stopped = await Reader.ReadAsync(repo.Path);
        Assert.NotNull(stopped.Operation);
        Assert.Single(stopped.Unmerged);

        repo.WriteFile("conflict.txt", "reconciled by hand\n");
        await RunActionAsync(
            repo, new ActionRequest("mark-resolved", Path: "conflict.txt"));

        var resolved = await Reader.ReadAsync(repo.Path);
        Assert.Empty(resolved.Unmerged);
        Assert.NotNull(resolved.Operation);

        var finished = await RunActionAsync(repo, new ActionRequest("merge-continue"));

        Assert.Null(finished.Operation);
        Assert.Contains(finished.RecentCommits, c => c.Subject.Contains("Merge"));
    }

    [Fact]
    public async Task MergeAbort_PutsTheFilesBackAgainstARealRepository()
    {
        using var repo = await TestRepo.CreateAsync();
        await repo.StartConflictingMergeAsync();

        var abandoned = await RunActionAsync(repo, new ActionRequest("merge-abort"));

        Assert.Null(abandoned.Operation);
        Assert.Empty(abandoned.Unmerged);
        Assert.Equal(
            "ours\n",
            File.ReadAllText(Path.Combine(repo.Path, "conflict.txt")).Replace("\r\n", "\n"));
    }

    [Fact]
    public void Rebase_BuildsRebaseOntoTheGivenBase()
    {
        var args = ActionCatalog.Find("rebase")!
            .BuildArgs(MinimalState(), new ActionRequest("rebase", BranchName: "main"));

        Assert.Equal(new[] { "rebase", "main" }, args);
    }

    [Fact]
    public void RebaseContinue_BuildsRebaseContinue()
    {
        var args = ActionCatalog.Find("rebase-continue")!
            .BuildArgs(MinimalState(), new ActionRequest("rebase-continue"));

        Assert.Equal(new[] { "rebase", "--continue" }, args);
    }

    [Fact]
    public void RebaseSkip_BuildsRebaseSkip()
    {
        var args = ActionCatalog.Find("rebase-skip")!
            .BuildArgs(MinimalState(), new ActionRequest("rebase-skip"));

        Assert.Equal(new[] { "rebase", "--skip" }, args);
    }

    [Fact]
    public void RebaseAbort_BuildsRebaseAbort()
    {
        var args = ActionCatalog.Find("rebase-abort")!
            .BuildArgs(MinimalState(), new ActionRequest("rebase-abort"));

        Assert.Equal(new[] { "rebase", "--abort" }, args);
    }

    [Fact]
    public void Rebase_AllPreconditionsPassWithoutAnUpstream()
    {
        // Positive control for Rebase_IsRefusedOnceTheBranchIsOnTheServer: BranchName differs
        // from MinimalState()'s Branch ("main"), so RequiresNotCurrentBranch cannot be the one
        // doing the refusing in that test. This proves every other precondition on rebase is
        // already satisfied by MinimalState() plus a non-colliding branch name, so the only
        // thing left that can fail once Upstream is set is RequiresNoUpstream itself.
        var request = new ActionRequest("rebase", BranchName: "feature");

        Assert.All(
            ActionCatalog.Find("rebase")!.Preconditions.Select(p => p.Evaluate(MinimalState(), request)),
            r => Assert.True(r.Satisfied, r.Message));
    }

    [Fact]
    public void Rebase_IsRefusedOnceTheBranchIsOnTheServer()
    {
        var pushed = MinimalState() with { Upstream = "origin/feature" };
        var request = new ActionRequest("rebase", BranchName: "feature");

        var failed = ActionCatalog.Find("rebase")!.Preconditions
            .Where(p => !p.Evaluate(pushed, request).Satisfied)
            .ToList();

        var failure = Assert.Single(failed);
        Assert.IsType<RequiresNoUpstream>(failure);
    }

    [Fact]
    public void MarkResolved_WorksDuringARebaseAndNotOnlyAMerge()
    {
        // Without this the user is left holding conflicted files in a rebase with no way to
        // mark them fixed and therefore no way to carry on.
        var rebasing = MinimalState() with
        {
            Operation = new OperationState(OperationKind.Rebase, "main"),
        };
        var request = new ActionRequest("mark-resolved", Path: "conflict.txt");

        Assert.All(
            ActionCatalog.Find("mark-resolved")!.Preconditions.Select(p => p.Evaluate(rebasing, request)),
            r => Assert.True(r.Satisfied, r.Message));
    }

    [Fact]
    public async Task Rebase_Stops_ThenResolves_ThenContinues_AgainstARealRepository()
    {
        using var repo = await TestRepo.CreateAsync();
        await repo.StartConflictingRebaseAsync();

        var stopped = await Reader.ReadAsync(repo.Path);
        Assert.Equal(OperationKind.Rebase, stopped.Operation!.Kind);
        Assert.Single(stopped.Unmerged);

        repo.WriteFile("conflict.txt", "reconciled by hand\n");
        await RunActionAsync(repo, new ActionRequest("mark-resolved", Path: "conflict.txt"));

        var finished = await RunActionAsync(repo, new ActionRequest("rebase-continue"));

        Assert.Null(finished.Operation);
        Assert.Contains(finished.RecentCommits, c => c.Subject == "my work");
        Assert.Contains(finished.RecentCommits, c => c.Subject == "their work");
    }

    [Fact]
    public async Task RebaseSkip_DropsTheCommitAgainstARealRepository()
    {
        using var repo = await TestRepo.CreateAsync();
        await repo.StartConflictingRebaseAsync();

        var after = await RunActionAsync(repo, new ActionRequest("rebase-skip"));

        Assert.Null(after.Operation);
        // The commit being replayed was skipped, so it is not in the branch any more.
        Assert.DoesNotContain(after.RecentCommits, c => c.Subject == "my work");
        Assert.Contains(after.RecentCommits, c => c.Subject == "their work");
    }

    [Fact]
    public async Task RebaseAbort_PutsTheBranchBackAgainstARealRepository()
    {
        using var repo = await TestRepo.CreateAsync();
        await repo.StartConflictingRebaseAsync();

        var after = await RunActionAsync(repo, new ActionRequest("rebase-abort"));

        Assert.Null(after.Operation);
        Assert.Contains(after.RecentCommits, c => c.Subject == "my work");
        Assert.DoesNotContain(after.RecentCommits, c => c.Subject == "their work");
    }

    [Fact]
    public void TheThreeDestructiveActionsAreTheOnesThatLoseWork()
    {
        var destructive = ActionCatalog.All.Where(a => a.Danger == Danger.Destructive).Select(a => a.Id);

        Assert.Equal(
            new[] { "discard-file", "stash-drop", "rebase-skip" }.OrderBy(x => x),
            destructive.OrderBy(x => x));
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
            Array.Empty<FileChange>(), Array.Empty<CommitInfo>(), Array.Empty<BranchInfo>(),
            Array.Empty<TagInfo>(), Array.Empty<StashInfo>(),
            Operation: null);

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
            Array.Empty<FileChange>(), Array.Empty<CommitInfo>(), Array.Empty<BranchInfo>(),
            Array.Empty<TagInfo>(), Array.Empty<StashInfo>(),
            Operation: null);

        var args = ActionCatalog.Find("pull")!.BuildArgs(state, new ActionRequest("pull"));

        Assert.Contains("--ff-only", args);
    }

    [Fact]
    public void DeleteBranch_NeverForceDeletes()
    {
        var state = new RepoState(
            @"C:\r", "main", false, null, 0, 0, true, false,
            Array.Empty<FileChange>(), Array.Empty<CommitInfo>(), Array.Empty<BranchInfo>(),
            Array.Empty<TagInfo>(), Array.Empty<StashInfo>(),
            Operation: null);

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
            Array.Empty<FileChange>(), Array.Empty<CommitInfo>(), Array.Empty<BranchInfo>(),
            Array.Empty<TagInfo>(), Array.Empty<StashInfo>(),
            Operation: null);
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

    [Fact]
    public void ConnectRemote_BuildsRemoteAddOriginWithTheTrimmedUrl()
    {
        var action = ActionCatalog.Find("connect-remote")!;
        var state = MinimalState();

        var args = action.BuildArgs(state, new ActionRequest(
            "connect-remote", RemoteUrl: "  https://github.com/me/project.git  "));

        Assert.Equal(
            new[] { "remote", "add", "origin", "https://github.com/me/project.git" }, args);
    }

    [Fact]
    public void DisconnectRemote_BuildsRemoteRemoveOrigin()
    {
        var action = ActionCatalog.Find("disconnect-remote")!;

        var args = action.BuildArgs(MinimalState(), new ActionRequest("disconnect-remote"));

        Assert.Equal(new[] { "remote", "remove", "origin" }, args);
    }

    [Fact]
    public void ConnectRemote_UndoesToDisconnectRemote()
    {
        Assert.Equal("disconnect-remote", ActionCatalog.Find("connect-remote")!.UndoActionId);
    }

    [Fact]
    public async Task ConnectRemote_ThenDisconnectRemote_RoundTripsAgainstARealRepository()
    {
        using var repo = await TestRepo.CreateAsync();

        var connected = await RunActionAsync(
            repo, new ActionRequest("connect-remote", RemoteUrl: "https://github.com/me/project.git"));
        Assert.True(connected.HasRemote);
        Assert.Contains("origin", (await repo.GitAsync("remote")).StdOut);

        var disconnected = await RunActionAsync(repo, new ActionRequest("disconnect-remote"));
        Assert.False(disconnected.HasRemote);
        Assert.Empty((await repo.GitAsync("remote")).StdOut.Trim());
    }

    [Fact]
    public void CreateTag_BuildsTagWithTheGivenName()
    {
        var args = ActionCatalog.Find("create-tag")!
            .BuildArgs(MinimalState(), new ActionRequest("create-tag", TagName: "v1"));

        Assert.Equal(new[] { "tag", "--", "v1" }, args);
    }

    [Fact]
    public void DeleteTag_BuildsTagDashD()
    {
        var args = ActionCatalog.Find("delete-tag")!
            .BuildArgs(MinimalState(), new ActionRequest("delete-tag", TagName: "v1"));

        Assert.Equal(new[] { "tag", "-d", "--", "v1" }, args);
    }

    [Fact]
    public void CreateTag_UndoesToDeleteTag()
    {
        Assert.Equal("delete-tag", ActionCatalog.Find("create-tag")!.UndoActionId);
    }

    [Fact]
    public async Task CreateTag_ThenDeleteTag_RoundTripsAgainstARealRepository()
    {
        using var repo = await TestRepo.CreateAsync();

        var tagged = await RunActionAsync(repo, new ActionRequest("create-tag", TagName: "v1"));
        Assert.Contains(tagged.Tags, t => t.Name == "v1");

        var untagged = await RunActionAsync(repo, new ActionRequest("delete-tag", TagName: "v1"));
        Assert.DoesNotContain(untagged.Tags, t => t.Name == "v1");
    }

    [Fact]
    public void Stash_BuildsStashPushWithoutAMessageWhenNoneGiven()
    {
        var args = ActionCatalog.Find("stash")!.BuildArgs(MinimalState(), new ActionRequest("stash"));

        Assert.Equal(new[] { "stash", "push" }, args);
    }

    [Fact]
    public void Stash_BuildsStashPushWithAMessageWhenOneIsGiven()
    {
        var args = ActionCatalog.Find("stash")!
            .BuildArgs(MinimalState(), new ActionRequest("stash", Message: "wip"));

        Assert.Equal(new[] { "stash", "push", "-m", "wip" }, args);
    }

    [Fact]
    public void StashPop_BuildsStashPopWithTheGivenRef()
    {
        var args = ActionCatalog.Find("stash-pop")!
            .BuildArgs(MinimalState(), new ActionRequest("stash-pop", StashRef: "stash@{0}"));

        Assert.Equal(new[] { "stash", "pop", "stash@{0}" }, args);
    }

    [Fact]
    public void StashApply_BuildsStashApplyWithTheGivenRef()
    {
        var args = ActionCatalog.Find("stash-apply")!
            .BuildArgs(MinimalState(), new ActionRequest("stash-apply", StashRef: "stash@{0}"));

        Assert.Equal(new[] { "stash", "apply", "stash@{0}" }, args);
    }

    [Fact]
    public void StashDrop_BuildsStashDropWithTheGivenRef()
    {
        var args = ActionCatalog.Find("stash-drop")!
            .BuildArgs(MinimalState(), new ActionRequest("stash-drop", StashRef: "stash@{0}"));

        Assert.Equal(new[] { "stash", "drop", "stash@{0}" }, args);
    }

    [Fact]
    public void Stash_UndoesToStashPop()
    {
        Assert.Equal("stash-pop", ActionCatalog.Find("stash")!.UndoActionId);
    }

    [Fact]
    public void Stash_RequiresCommitsBeforeOfferingToSetAnythingAside()
    {
        var preconditions = ActionCatalog.Find("stash")!.Preconditions;

        Assert.Contains(preconditions, p => p is RequiresCommits);
    }

    [Fact]
    public async Task Stash_ThenStashPop_RoundTripsAgainstARealRepository()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("README.md", "changed\n");

        var stashed = await RunActionAsync(repo, new ActionRequest("stash", Message: "wip"));
        Assert.Single(stashed.Stashes);
        Assert.False(stashed.HasUncommittedChanges);

        var popped = await RunActionAsync(
            repo, new ActionRequest("stash-pop", StashRef: stashed.Stashes[0].Ref));
        Assert.Empty(popped.Stashes);
        Assert.True(popped.HasUncommittedChanges);
    }

    [Fact]
    public async Task Stash_ThenStashDrop_RemovesTheEntryWithoutRestoringChanges()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("README.md", "changed\n");

        var stashed = await RunActionAsync(repo, new ActionRequest("stash", Message: "wip"));

        var dropped = await RunActionAsync(
            repo, new ActionRequest("stash-drop", StashRef: stashed.Stashes[0].Ref));
        Assert.Empty(dropped.Stashes);
        Assert.False(dropped.HasUncommittedChanges);
    }

    /// <summary>A minimal state for descriptors that read nothing out of it.</summary>
    private static RepoState MinimalState() => new(
        RepoRoot: @"C:\r", Branch: "main", IsDetached: false, Upstream: null,
        Ahead: 0, Behind: 0, HasCommits: true, HasRemote: false,
        Changes: Array.Empty<FileChange>(),
        RecentCommits: Array.Empty<CommitInfo>(),
        Branches: Array.Empty<BranchInfo>(),
        Tags: Array.Empty<TagInfo>(),
        Stashes: Array.Empty<StashInfo>(),
        Operation: null);
}
