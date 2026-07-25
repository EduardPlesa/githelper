using GitHelper.Core.Actions;
using GitHelper.Core.Content;
using GitHelper.Core.Git;
using GitHelper.Core.Repo;

namespace GitHelper.Core.Tests;

public class ActionServiceTests
{
    private static ActionService NewService()
    {
        var runner = new GitRunner();
        return new ActionService(runner, new RepoStateReader(runner), ContentLibrary.Load());
    }

    [Fact]
    public async Task PreviewAsync_ShowsTheExactCommandWithoutRunningIt()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        await repo.GitAsync("add", "-A");

        var preview = await NewService().PreviewAsync(
            repo.Path, new ActionRequest("commit", Message: "add a file"));

        Assert.Equal("git commit -m add a file", preview.CommandLine);
        Assert.True(preview.CanRun);

        // Nothing ran: the history is still just the initial commit.
        var log = await repo.GitAsync("log", "--oneline");
        Assert.Single(log.StdOut.Trim().Split('\n'));
    }

    [Fact]
    public async Task PreviewAsync_BindsLiveValuesIntoTheExplanation()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        repo.WriteFile("b.txt", "y\n");
        await repo.GitAsync("add", "-A");

        var preview = await NewService().PreviewAsync(
            repo.Path, new ActionRequest("commit", Message: "two files"));

        Assert.Equal("2", preview.Slots["stagedCount"]);
        Assert.Equal("main", preview.Slots["branch"]);
        Assert.Equal("commit", preview.Explanation.Id);
    }

    [Fact]
    public async Task PreviewAsync_ReportsBlockersWithoutRunningAnything()
    {
        using var repo = await TestRepo.CreateAsync();

        var preview = await NewService().PreviewAsync(
            repo.Path, new ActionRequest("commit", Message: "nothing staged"));

        Assert.False(preview.CanRun);
        Assert.Contains(preview.Blockers, b => b.SuggestedActionId == "stage-all");
    }

    [Fact]
    public async Task PreviewAsync_CarriesTheDangerLevelAndUndoHint()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("README.md", "changed\n");

        var preview = await NewService().PreviewAsync(
            repo.Path, new ActionRequest("discard-file", Path: "README.md"));

        Assert.Equal(Danger.Destructive, preview.Danger);
        Assert.NotEmpty(preview.Explanation.Undo);
    }

    [Fact]
    public async Task RunAsync_ExecutesAndNarratesTheObservedChange()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        await repo.GitAsync("add", "-A");

        var outcome = await NewService().RunAsync(
            repo.Path, new ActionRequest("commit", Message: "add a file"));

        Assert.True(outcome.Success);
        Assert.Contains("add a file", outcome.Narration!);
        Assert.Equal(2, outcome.After.RecentCommits.Count);
        Assert.Single(outcome.Before.RecentCommits);
    }

    [Fact]
    public async Task RunAsync_RevalidatesPreconditionsAndRefusesToRunGit()
    {
        using var repo = await TestRepo.CreateAsync();

        var outcome = await NewService().RunAsync(
            repo.Path, new ActionRequest("commit", Message: "nothing is staged"));

        Assert.False(outcome.Success);
        Assert.NotEmpty(outcome.Blockers);
        Assert.Equal(0, outcome.Result.ExitCode);
        Assert.Empty(outcome.Result.ArgVector); // no command was built or run
        Assert.Single(outcome.After.RecentCommits);
    }

    [Fact]
    public async Task RunAsync_TranslatesAFailureFromGit()
    {
        using var repo = await TestRepo.CreateAsync();

        // No remote is configured, so push fails at the git level rather than at a precondition.
        await repo.GitAsync("remote", "add", "origin", "https://example.invalid/nope.git");
        var outcome = await NewService().RunAsync(repo.Path, new ActionRequest("push"));

        Assert.False(outcome.Success);
        Assert.NotNull(outcome.Error);
        Assert.NotEmpty(outcome.Error!.RawOutput);
    }

    [Fact]
    public async Task RunAsync_RejectsAnUnknownActionId()
    {
        using var repo = await TestRepo.CreateAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => NewService().RunAsync(repo.Path, new ActionRequest("no-such-action")));
    }
}
