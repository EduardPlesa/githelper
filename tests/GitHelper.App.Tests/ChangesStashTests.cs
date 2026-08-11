using GitHelper.App.ViewModels;
using GitHelper.Core.Actions;
using GitHelper.Core.Content;
using GitHelper.Core.Git;
using GitHelper.Core.Model;
using GitHelper.Core.Repo;

namespace GitHelper.App.Tests;

/// <summary>The Stash section of the Changes tab: setting changes aside, and getting them back.</summary>
public class ChangesStashTests
{
    private sealed record Fixture(
        ChangesViewModel Changes, ExplainPanelViewModel Panel, StubConfirmationDialog Confirmations);

    private static Fixture NewFixture()
    {
        var runner = new GitRunner();
        var reader = new RepoStateReader(runner);
        var service = new ActionService(runner, reader, ContentLibrary.Load());
        var confirmations = new StubConfirmationDialog();
        var panel = new ExplainPanelViewModel(service, confirmations, new InMemorySettingsStore());
        return new Fixture(new ChangesViewModel(panel), panel, confirmations);
    }

    [Fact]
    public void CanStash_ReflectsWhetherThereAreUncommittedChanges()
    {
        var f = NewFixture();
        var dirty = new RepoState(
            RepoRoot: @"C:\r", Branch: "main", IsDetached: false, Upstream: null,
            Ahead: 0, Behind: 0, HasCommits: true, HasRemote: false,
            Changes: new[] { new FileChange("a.txt", null, ChangeKind.None, ChangeKind.Modified) },
            RecentCommits: Array.Empty<CommitInfo>(), Branches: Array.Empty<BranchInfo>(),
            Tags: Array.Empty<TagInfo>(), Stashes: Array.Empty<StashInfo>());

        f.Changes.Update(dirty, null);
        Assert.True(f.Changes.CanStash);

        f.Changes.Update(dirty with { Changes = Array.Empty<FileChange>() }, null);
        Assert.False(f.Changes.CanStash);
    }

    [Fact]
    public async Task StashCommand_ThenListedInStashes()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("README.md", "changed\n");
        var f = NewFixture();
        var reader = new RepoStateReader(new GitRunner());
        f.Changes.Update(await reader.ReadAsync(repo.Path), null);
        f.Changes.StashMessage = "wip";

        // stash is Safe, so ShowAndRunIfUngated runs it straight away.
        await f.Changes.StashCommand.ExecuteAsync(null);

        var after = await reader.ReadAsync(repo.Path);
        Assert.Single(after.Stashes);
        Assert.False(after.HasUncommittedChanges);
    }

    [Fact]
    public async Task PopCommand_ThenConfirming_RestoresTheChangesAndRemovesTheEntry()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("README.md", "changed\n");
        await repo.GitAsync("stash", "push", "-m", "wip");
        var f = NewFixture();
        var reader = new RepoStateReader(new GitRunner());
        f.Changes.Update(await reader.ReadAsync(repo.Path), null);

        await f.Changes.Stashes.Single().PopCommand.ExecuteAsync(null);
        await f.Panel.RunAsync();

        var after = await reader.ReadAsync(repo.Path);
        Assert.Empty(after.Stashes);
        Assert.True(after.HasUncommittedChanges);
    }

    [Fact]
    public async Task DropCommand_ThenConfirming_RemovesTheEntry()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("README.md", "changed\n");
        await repo.GitAsync("stash", "push", "-m", "wip");
        var f = NewFixture();
        f.Confirmations.NextAnswer = true;
        var reader = new RepoStateReader(new GitRunner());
        f.Changes.Update(await reader.ReadAsync(repo.Path), null);

        // stash-drop is Destructive: the modal is the gate, so this one call both previews
        // and runs it, the same way DiscardCommand does.
        await f.Changes.Stashes.Single().DropCommand.ExecuteAsync(null);

        Assert.Equal(1, f.Confirmations.CallCount);
        var after = await reader.ReadAsync(repo.Path);
        Assert.Empty(after.Stashes);
        Assert.Contains("stashed", f.Confirmations.LastConsequence!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OnActionCompleted_ClearsTheStashMessageBoxOnlyWhenAStashAppeared()
    {
        var f = NewFixture();
        f.Changes.StashMessage = "wip";

        var clean = new RepoState(
            RepoRoot: @"C:\r", Branch: "main", IsDetached: false, Upstream: null,
            Ahead: 0, Behind: 0, HasCommits: true, HasRemote: false,
            Changes: Array.Empty<FileChange>(), RecentCommits: Array.Empty<CommitInfo>(),
            Branches: Array.Empty<BranchInfo>(), Tags: Array.Empty<TagInfo>(),
            Stashes: Array.Empty<StashInfo>());
        var stashed = clean with { Stashes = new[] { new StashInfo("stash@{0}", "On main: wip") } };

        f.Changes.OnActionCompleted(new ActionOutcome(
            Success: true,
            Result: new GitCommandResult(new[] { "stash", "push" }, "", "", 0, TimeSpan.Zero),
            Narration: "set aside",
            Error: null,
            Before: clean,
            After: stashed,
            Blockers: Array.Empty<PreconditionResult>()));

        Assert.Equal(string.Empty, f.Changes.StashMessage);
    }
}
