using GitHelper.App.ViewModels;
using GitHelper.Core.Actions;
using GitHelper.Core.Content;
using GitHelper.Core.Git;
using GitHelper.Core.Model;
using GitHelper.Core.Repo;

namespace GitHelper.App.Tests;

/// <summary>
/// The commit -> send handoff on the Changes tab.
///
/// Push was only ever reachable from the Branches tab, so committing left the user with no
/// indication that the work was still local. These cover when the prompt appears, when it
/// stays out of the way, and that it drives the same push action rather than a parallel one.
/// </summary>
public class ChangesPushPromptTests
{
    private sealed record Fixture(ChangesViewModel Changes, ExplainPanelViewModel Panel);

    private static Fixture NewFixture()
    {
        var runner = new GitRunner();
        var reader = new RepoStateReader(runner);
        var service = new ActionService(runner, reader, ContentLibrary.Load());
        var panel = new ExplainPanelViewModel(
            service, new StubConfirmationDialog(), new InMemorySettingsStore());
        return new Fixture(new ChangesViewModel(panel), panel);
    }

    private static RepoState State(
        int ahead = 0,
        string? upstream = "origin/main",
        bool hasRemote = true,
        bool hasCommits = true,
        bool isDetached = false) => new(
            RepoRoot: @"C:\r",
            Branch: isDetached ? null : "main",
            IsDetached: isDetached,
            Upstream: upstream,
            Ahead: ahead,
            Behind: 0,
            HasCommits: hasCommits,
            HasRemote: hasRemote,
            Changes: Array.Empty<FileChange>(),
            RecentCommits: Array.Empty<CommitInfo>(),
            Branches: Array.Empty<BranchInfo>());

    [Fact]
    public void StaysHiddenWhenTheBranchIsInStepWithTheServer()
    {
        var f = NewFixture();

        f.Changes.Update(State(ahead: 0), null);

        Assert.False(f.Changes.HasUnpushedCommits);
        Assert.Equal(string.Empty, f.Changes.UnpushedSummary);
    }

    [Fact]
    public void AppearsOnceThereIsSomethingToSend()
    {
        var f = NewFixture();

        f.Changes.Update(State(ahead: 1), null);

        Assert.True(f.Changes.HasUnpushedCommits);
        Assert.Equal("1 commit to send", f.Changes.UnpushedSummary);
    }

    [Fact]
    public void CountsMoreThanOneInThePlural()
    {
        var f = NewFixture();

        f.Changes.Update(State(ahead: 3), null);

        Assert.Equal("3 commits to send", f.Changes.UnpushedSummary);
    }

    [Fact]
    public void SaysSoWhenTheBranchHasNeverBeenSent()
    {
        // No upstream means no ahead count exists -- but this is the case that matters most,
        // because none of the branch is on the server at all.
        var f = NewFixture();

        f.Changes.Update(State(ahead: 0, upstream: null), null);

        Assert.True(f.Changes.HasUnpushedCommits);
        Assert.Contains("not on the server", f.Changes.UnpushedSummary);
    }

    [Fact]
    public void StaysHiddenWithNoRemoteAtAll()
    {
        // There is nowhere to send to, so offering the button would only ever produce a
        // blocked-action message.
        var f = NewFixture();

        f.Changes.Update(State(ahead: 2, upstream: null, hasRemote: false), null);

        Assert.False(f.Changes.HasUnpushedCommits);
    }

    [Fact]
    public void StaysHiddenBeforeTheFirstCommit()
    {
        var f = NewFixture();

        f.Changes.Update(State(upstream: null, hasCommits: false), null);

        Assert.False(f.Changes.HasUnpushedCommits);
    }

    [Fact]
    public void StaysHiddenInDetachedHead()
    {
        // push is blocked by RequiresNotDetached, so the prompt must not offer it.
        var f = NewFixture();

        f.Changes.Update(State(ahead: 1, isDetached: true), null);

        Assert.False(f.Changes.HasUnpushedCommits);
    }

    [Fact]
    public async Task PushCommandDrivesTheSamePushActionThroughTheExplainPanel()
    {
        using var repo = await TestRepo.CreateAsync();
        var f = NewFixture();
        f.Changes.Update(await new RepoStateReader(new GitRunner()).ReadAsync(repo.Path), null);

        await f.Changes.PushCommand.ExecuteAsync(null);

        // Previewed rather than run: push is Caution, so it waits for an inline Confirm --
        // the same gate the Branches tab gets, not a shortcut around it.
        Assert.Equal("Send changes to the server", f.Panel.Title);
        Assert.True(f.Panel.RequiresInlineConfirmation);
    }

    [Fact]
    public async Task PushCommandIsBlockedWithAReadableReasonInDetachedHead()
    {
        // The prompt already hides itself here, so this is defence in depth: if the command is
        // reached anyway, the engine's RequiresNotDetached must produce an explanation rather
        // than a crash. Moved here with the button, from the Branches tab.
        using var repo = await TestRepo.CreateAsync();
        await repo.GitAsync("remote", "add", "origin", "https://example.invalid/nope.git");
        var head = (await repo.GitAsync("rev-parse", "HEAD")).StdOut.Trim();
        await repo.GitAsync("checkout", "-q", head);
        var f = NewFixture();
        f.Changes.Update(await new RepoStateReader(new GitRunner()).ReadAsync(repo.Path), null);

        await f.Changes.PushCommand.ExecuteAsync(null);

        Assert.False(f.Panel.CanRun);
        Assert.Contains(f.Panel.Blockers, b => b.Contains("detached", StringComparison.OrdinalIgnoreCase));
    }
}
