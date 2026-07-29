using GitHelper.App.ViewModels;
using GitHelper.Core.Actions;
using GitHelper.Core.Content;
using GitHelper.Core.Git;
using GitHelper.Core.Repo;

namespace GitHelper.App.Tests;

/// <summary>
/// A local repository with one commit, taken as far as a test can go: the offer appears,
/// the user opens GitHub, pastes an address, confirms, and origin is set afterwards. The
/// send itself needs a network and a credential helper, so it stops there.
/// </summary>
public class PublishJourneyTests
{
    // Identical across both tests: a real runner over the temp repo, feeding a reader and an
    // explain panel wired the same way the app wires them. What each test does with that
    // panel — and whether it names its own browser stub — differs, so that part stays inline.
    private static (GitRunner Runner, RepoStateReader Reader, ExplainPanelViewModel Panel) NewPanel()
    {
        var runner = new GitRunner();
        var reader = new RepoStateReader(runner);
        var panel = new ExplainPanelViewModel(
            new ActionService(runner, reader, ContentLibrary.Load()),
            new StubConfirmationDialog(),
            new InMemorySettingsStore());
        return (runner, reader, panel);
    }

    [Fact]
    public async Task ARepositoryWithNoRemoteIsOfferedTheConnectFlowAndEndsUpWithOrigin()
    {
        using var repo = await TestRepo.CreateAsync();
        var (_, reader, panel) = NewPanel();
        var browser = new StubBrowserLauncher();
        var changes = new ChangesViewModel(panel, browser);

        var before = await reader.ReadAsync(repo.Path);
        changes.Update(before, null);

        // 1. The app says the project is not on GitHub, and nothing else.
        Assert.True(changes.HasNoRemoteOffer);
        Assert.False(changes.HasUnpushedCommits);

        // 2. The user creates the repository themselves; the app only opens the page.
        changes.OpenGitHubCommand.Execute(null);
        Assert.Equal(ChangesViewModel.NewRepositoryUrl, browser.LastUrl);

        // 3. Pasting an address previews the command rather than running it.
        changes.RemoteUrl = "https://github.com/me/project.git";
        await changes.ConnectRemoteCommand.ExecuteAsync(null);
        Assert.True(panel.CanRun);
        Assert.True(panel.RequiresInlineConfirmation);
        Assert.Empty((await repo.GitAsync("remote")).StdOut.Trim());

        // 4. Confirming runs it.
        Assert.True(await panel.RunAsync());

        var after = await reader.ReadAsync(repo.Path);
        Assert.True(after.HasRemote);
        Assert.Contains("origin", (await repo.GitAsync("remote")).StdOut);
        Assert.Contains(
            "https://github.com/me/project.git",
            (await repo.GitAsync("remote", "get-url", "origin")).StdOut);

        // The branch is exactly where it was: connecting writes an address, not history.
        Assert.Equal(before.Branch, after.Branch);
        Assert.Equal(before.RecentCommits.Count, after.RecentCommits.Count);
        // Still not on the server — connecting is not sending.
        Assert.Null(after.Upstream);
    }

    [Fact]
    public async Task ARejectedAddressBlocksTheActionWithoutTouchingWhatWasTyped()
    {
        // What this proves is the preview path: a page URL is refused by validation, and
        // nothing on the way there disturbs the box. The separate question — that a failed
        // *run* keeps the text — is settled in ChangesConnectRemoteTests, because it needs
        // an outcome that only a completed run produces.
        using var repo = await TestRepo.CreateAsync();
        var (_, reader, panel) = NewPanel();
        var changes = new ChangesViewModel(panel, new StubBrowserLauncher());
        changes.Update(await reader.ReadAsync(repo.Path), null);

        changes.RemoteUrl = "https://github.com/me/project/tree/main";
        await changes.ConnectRemoteCommand.ExecuteAsync(null);

        Assert.False(panel.CanRun);
        Assert.Equal("https://github.com/me/project/tree/main", changes.RemoteUrl);
    }
}
