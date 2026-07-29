using GitHelper.App.ViewModels;
using GitHelper.Core.Actions;
using GitHelper.Core.Content;
using GitHelper.Core.Git;
using GitHelper.Core.Model;
using GitHelper.Core.Repo;

namespace GitHelper.App.Tests;

/// <summary>
/// The third state of the push prompt: this project is not on GitHub at all. Covers when it
/// appears, that the GitHub button opens the right page, and that connecting is previewed
/// through the same explain panel as everything else rather than run on click.
/// </summary>
public class ChangesConnectRemoteTests
{
    private sealed record Fixture(
        ChangesViewModel Changes, ExplainPanelViewModel Panel, StubBrowserLauncher Browser);

    private static Fixture NewFixture()
    {
        var runner = new GitRunner();
        var reader = new RepoStateReader(runner);
        var service = new ActionService(runner, reader, ContentLibrary.Load());
        var panel = new ExplainPanelViewModel(
            service, new StubConfirmationDialog(), new InMemorySettingsStore());
        var browser = new StubBrowserLauncher();
        return new Fixture(new ChangesViewModel(panel, browser), panel, browser);
    }

    private static RepoState State(bool hasRemote, string? upstream = null, int ahead = 0) => new(
        RepoRoot: @"C:\r", Branch: "main", IsDetached: false, Upstream: upstream,
        Ahead: ahead, Behind: 0, HasCommits: true, HasRemote: hasRemote,
        Changes: Array.Empty<FileChange>(),
        RecentCommits: Array.Empty<CommitInfo>(),
        Branches: Array.Empty<BranchInfo>());

    [Fact]
    public void TheOfferAppearsWhenNothingIsConnected()
    {
        var f = NewFixture();

        f.Changes.Update(State(hasRemote: false), null);

        Assert.True(f.Changes.HasNoRemoteOffer);
    }

    [Fact]
    public void TheOfferDisappearsOnceARemoteExists()
    {
        var f = NewFixture();

        f.Changes.Update(State(hasRemote: true, upstream: "origin/main", ahead: 1), null);

        Assert.False(f.Changes.HasNoRemoteOffer);
    }

    [Fact]
    public void TheConnectOfferAndTheSendPromptAreNeverBothShowing()
    {
        var f = NewFixture();

        f.Changes.Update(State(hasRemote: false), null);

        Assert.True(f.Changes.HasNoRemoteOffer);
        Assert.False(f.Changes.HasUnpushedCommits);
    }

    [Fact]
    public void TheGitHubButtonOpensTheNewRepositoryPage()
    {
        var f = NewFixture();

        f.Changes.OpenGitHubCommand.Execute(null);

        Assert.Equal("https://github.com/new", f.Browser.LastUrl);
    }

    [Fact]
    public async Task ConnectingIsPreviewedRatherThanRunOnClick()
    {
        using var repo = await TestRepo.CreateAsync();
        var f = NewFixture();
        f.Changes.Update(await new RepoStateReader(new GitRunner()).ReadAsync(repo.Path), null);
        f.Changes.RemoteUrl = "https://github.com/me/project.git";

        await f.Changes.ConnectRemoteCommand.ExecuteAsync(null);

        Assert.Equal("Connect to GitHub", f.Panel.Title);
        Assert.True(f.Panel.RequiresInlineConfirmation);
        Assert.Contains("remote add origin", f.Panel.CommandLine);
        // Nothing ran: the preview stops at the inline Confirm.
        Assert.Empty((await repo.GitAsync("remote")).StdOut.Trim());
    }

    [Fact]
    public async Task AnUnusableAddressIsBlockedWithAReadableReason()
    {
        using var repo = await TestRepo.CreateAsync();
        var f = NewFixture();
        f.Changes.Update(await new RepoStateReader(new GitRunner()).ReadAsync(repo.Path), null);
        f.Changes.RemoteUrl = "--upload-pack=calc.exe";

        await f.Changes.ConnectRemoteCommand.ExecuteAsync(null);

        Assert.False(f.Panel.CanRun);
        Assert.Contains(f.Panel.Blockers, b => b.Contains("dash", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheAddressBoxClearsOnceARemoteHasAppeared()
    {
        var f = NewFixture();
        f.Changes.RemoteUrl = "https://github.com/me/project.git";

        f.Changes.OnActionCompleted(new ActionOutcome(
            Success: true,
            Result: new GitCommandResult(Array.Empty<string>(), "", "", 0, TimeSpan.Zero),
            Narration: null,
            Error: null,
            Before: State(hasRemote: false),
            After: State(hasRemote: true),
            Blockers: Array.Empty<PreconditionResult>()));

        Assert.Equal(string.Empty, f.Changes.RemoteUrl);
    }

    [Fact]
    public void TheAddressBoxKeepsWhatWasTypedWhenTheConnectFailed()
    {
        // The counterpart of the test above, and the more important half: clearing is driven
        // by a remote observably appearing, so an address git rejected stays put for the user
        // to correct rather than vanishing along with the failure.
        var f = NewFixture();
        f.Changes.RemoteUrl = "https://github.com/me/typo.git";

        f.Changes.OnActionCompleted(new ActionOutcome(
            Success: false,
            Result: new GitCommandResult(Array.Empty<string>(), "", "", 1, TimeSpan.Zero),
            Narration: null,
            Error: null,
            Before: State(hasRemote: false),
            After: State(hasRemote: false),
            Blockers: Array.Empty<PreconditionResult>()));

        Assert.Equal("https://github.com/me/typo.git", f.Changes.RemoteUrl);
    }
}
