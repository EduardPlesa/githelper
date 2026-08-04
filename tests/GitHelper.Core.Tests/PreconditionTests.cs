using GitHelper.Core.Actions;
using GitHelper.Core.Model;

namespace GitHelper.Core.Tests;

public class PreconditionTests
{
    private static RepoState State(
        string? branch = "main",
        string? upstream = "origin/main",
        bool hasRemote = true,
        bool hasCommits = true,
        int commitCount = 2,
        TagInfo[]? tags = null,
        params FileChange[] changes)
        => new(
            RepoRoot: @"C:\repos\demo",
            Branch: branch,
            IsDetached: branch is null,
            Upstream: upstream,
            Ahead: 0,
            Behind: 0,
            HasCommits: hasCommits,
            HasRemote: hasRemote,
            Changes: changes,
            RecentCommits: Enumerable.Range(0, commitCount)
                .Select(i => new CommitInfo($"h{i}", $"h{i}", "A", DateTimeOffset.UnixEpoch, $"c{i}"))
                .ToList(),
            Branches: new[] { new BranchInfo("main", "origin/main"), new BranchInfo("feature", null) },
            Tags: tags ?? new[] { new TagInfo("v1", "abc1234") },
            Stashes: Array.Empty<StashInfo>());

    private static ActionRequest Request(
        string? path = null, string? message = null, string? branchName = null,
        string? tagName = null, string? stashRef = null)
        => new("test", path, message, branchName, TagName: tagName, StashRef: stashRef);

    [Fact]
    public void RequiresPath_FailsWhenNoPathGiven()
    {
        Assert.False(new RequiresPath().Evaluate(State(), Request()).Satisfied);
        Assert.True(new RequiresPath().Evaluate(State(), Request(path: "a.txt")).Satisfied);
    }

    [Fact]
    public void RequiresMessage_FailsOnEmptyOrWhitespaceMessage()
    {
        Assert.False(new RequiresMessage().Evaluate(State(), Request(message: "   ")).Satisfied);
        Assert.True(new RequiresMessage().Evaluate(State(), Request(message: "fix things")).Satisfied);
    }

    [Fact]
    public void RequiresStagedChanges_ExplainsStagingWhenNothingIsStaged()
    {
        var nothingStaged = State(changes: new FileChange("a.txt", null, ChangeKind.None, ChangeKind.Modified));

        var result = new RequiresStagedChanges().Evaluate(nothingStaged, Request());

        Assert.False(result.Satisfied);
        Assert.Contains("staged", result.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("stage-all", result.SuggestedActionId);
    }

    [Fact]
    public void RequiresStagedChanges_PassesWhenSomethingIsStaged()
    {
        var staged = State(changes: new FileChange("a.txt", null, ChangeKind.Modified, ChangeKind.None));

        Assert.True(new RequiresStagedChanges().Evaluate(staged, Request()).Satisfied);
    }

    [Fact]
    public void RequiresParentCommit_FailsOnTheVeryFirstCommit()
    {
        var onlyOne = State(commitCount: 1);

        var result = new RequiresParentCommit().Evaluate(onlyOne, Request());

        Assert.False(result.Satisfied);
        Assert.Contains("first", result.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RequiresParentCommit_PassesWhenAParentExists()
    {
        Assert.True(new RequiresParentCommit().Evaluate(State(commitCount: 2), Request()).Satisfied);
    }

    [Fact]
    public void RequiresUpstream_SuggestsPushWhichCanSetIt()
    {
        var result = new RequiresUpstream().Evaluate(State(upstream: null), Request());

        Assert.False(result.Satisfied);
        Assert.Contains("upstream", result.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("push", result.SuggestedActionId);
    }

    [Fact]
    public void RequiresRemote_FailsWhenNoRemoteIsConfigured()
    {
        Assert.False(new RequiresRemote().Evaluate(State(hasRemote: false), Request()).Satisfied);
    }

    [Fact]
    public void RequiresNoUncommittedChanges_FailsAndSuggestsCommitting()
    {
        var dirty = State(changes: new FileChange("a.txt", null, ChangeKind.None, ChangeKind.Modified));

        var result = new RequiresNoUncommittedChanges().Evaluate(dirty, Request());

        Assert.False(result.Satisfied);
        Assert.Equal("commit", result.SuggestedActionId);
    }

    [Fact]
    public void RequiresNoUncommittedChanges_IgnoresUntrackedFilesWhichSwitchingCannotDisturb()
    {
        var untrackedOnly = State(changes: new FileChange("new.txt", null, ChangeKind.None, ChangeKind.Untracked));

        Assert.True(new RequiresNoUncommittedChanges().Evaluate(untrackedOnly, Request()).Satisfied);
    }

    [Fact]
    public void RequiresNotDetached_BlocksActionsThatNeedABranchName()
    {
        var detached = State(branch: null);

        var result = new RequiresNotDetached().Evaluate(detached, Request());

        Assert.False(result.Satisfied);
        Assert.Contains("detached", result.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RequiresNotDetached_PassesWhenOnABranch()
    {
        Assert.True(new RequiresNotDetached().Evaluate(State(), Request()).Satisfied);
    }

    [Fact]
    public void RequiresNotCurrentBranch_RefusesToDeleteTheBranchYouAreOn()
    {
        var result = new RequiresNotCurrentBranch().Evaluate(State(), Request(branchName: "main"));

        Assert.False(result.Satisfied);
        Assert.True(new RequiresNotCurrentBranch().Evaluate(State(), Request(branchName: "feature")).Satisfied);
    }

    [Fact]
    public void RequiresBranchDoesNotExist_RefusesADuplicateName()
    {
        Assert.False(new RequiresBranchDoesNotExist().Evaluate(State(), Request(branchName: "feature")).Satisfied);
        Assert.True(new RequiresBranchDoesNotExist().Evaluate(State(), Request(branchName: "brand-new")).Satisfied);
    }

    [Fact]
    public void RequiresTagName_FailsWhenNoTagNameGiven()
    {
        Assert.False(new RequiresTagName().Evaluate(State(), Request()).Satisfied);
        Assert.True(new RequiresTagName().Evaluate(State(), Request(tagName: "v2")).Satisfied);
    }

    [Fact]
    public void RequiresTagDoesNotExist_RefusesADuplicateName()
    {
        Assert.False(
            new RequiresTagDoesNotExist().Evaluate(State(), Request(tagName: "v1")).Satisfied);
        Assert.True(
            new RequiresTagDoesNotExist().Evaluate(State(), Request(tagName: "v2")).Satisfied);
    }

    [Fact]
    public void RequiresUncommittedChanges_FailsOnACleanTree()
    {
        Assert.False(new RequiresUncommittedChanges().Evaluate(State(), Request()).Satisfied);
    }

    [Fact]
    public void RequiresUncommittedChanges_PassesWhenSomethingIsUnstaged()
    {
        var dirty = State(changes: new FileChange("a.txt", null, ChangeKind.None, ChangeKind.Modified));

        Assert.True(new RequiresUncommittedChanges().Evaluate(dirty, Request()).Satisfied);
    }

    [Fact]
    public void RequiresStashRef_FailsWhenNoStashIsPicked()
    {
        Assert.False(new RequiresStashRef().Evaluate(State(), Request()).Satisfied);
        Assert.True(
            new RequiresStashRef().Evaluate(State(), Request(stashRef: "stash@{0}")).Satisfied);
    }

    [Fact]
    public void RequiresCommits_FailsInARepositoryWithNoCommitsYet()
    {
        Assert.False(new RequiresCommits().Evaluate(State(hasCommits: false), Request()).Satisfied);
    }

    [Fact]
    public void EveryFailureMessageIsNonEmptyUserFacingCopy()
    {
        // RequiresBranchName is excluded here: it only fails on a missing branch name,
        // which is the opposite of what RequiresNotCurrentBranch and
        // RequiresBranchDoesNotExist need in order to fail. It is asserted separately below.
        IPrecondition[] all =
        {
            new RequiresPath(), new RequiresMessage(),
            new RequiresCommits(), new RequiresParentCommit(), new RequiresStagedChanges(),
            new RequiresRemote(), new RequiresUpstream(), new RequiresNoUncommittedChanges(),
            new RequiresNotCurrentBranch(), new RequiresBranchDoesNotExist(),
            new RequiresTagDoesNotExist(), new RequiresStashRef(),
        };

        // A state and request chosen so that every precondition above fails.
        var failing = State(
            branch: "main", upstream: null, hasRemote: false, hasCommits: false, commitCount: 1,
            tags: new[] { new TagInfo("main", "abc1234") },
            changes: new FileChange("a.txt", null, ChangeKind.None, ChangeKind.Modified));
        var request = Request(branchName: "main", tagName: "main");

        foreach (var precondition in all)
        {
            var result = precondition.Evaluate(failing, request);
            Assert.False(result.Satisfied, $"{precondition.GetType().Name} unexpectedly passed");
            Assert.False(string.IsNullOrWhiteSpace(result.Message));
        }

        var branchNameResult = new RequiresBranchName().Evaluate(failing, Request());
        Assert.False(branchNameResult.Satisfied);
        Assert.False(string.IsNullOrWhiteSpace(branchNameResult.Message));
    }

    private static ActionRequest UrlRequest(string? url)
        => new("connect-remote", RemoteUrl: url);

    [Fact]
    public void RequiresNoRemote_BlocksASecondConnectAndPointsAtDisconnecting()
    {
        var result = new RequiresNoRemote().Evaluate(State(hasRemote: true), UrlRequest("https://x/y.git"));

        Assert.False(result.Satisfied);
        Assert.Equal("disconnect-remote", result.SuggestedActionId);
    }

    [Fact]
    public void RequiresNoRemote_PassesWhenNothingIsConnectedYet()
    {
        Assert.True(
            new RequiresNoRemote().Evaluate(State(hasRemote: false), UrlRequest("https://x/y.git")).Satisfied);
    }

    [Theory]
    [InlineData("https://github.com/me/project.git")]
    [InlineData("https://github.com/me/project")]
    [InlineData("git@github.com:me/project.git")]
    public void RequiresValidRemoteUrl_AcceptsTheTwoFormsGitHubOffers(string url)
    {
        Assert.True(new RequiresValidRemoteUrl().Evaluate(State(hasRemote: false), UrlRequest(url)).Satisfied);
    }

    [Fact]
    public void RequiresValidRemoteUrl_RejectsALeadingDashAsAnInstructionRatherThanAPlace()
    {
        // Argument injection: argv arrays stop shell injection, but git still reads a
        // leading '-' as a flag. This is the shape that matters.
        var result = new RequiresValidRemoteUrl()
            .Evaluate(State(hasRemote: false), UrlRequest("--upload-pack=calc.exe"));

        Assert.False(result.Satisfied);
        Assert.Contains("dash", result.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RequiresValidRemoteUrl_RejectsAPageInsideTheProjectWithAReadableExplanation()
    {
        // The common wrong paste: whatever the user was looking at on github.com.
        var result = new RequiresValidRemoteUrl()
            .Evaluate(State(hasRemote: false), UrlRequest("https://github.com/me/project/tree/main"));

        Assert.False(result.Satisfied);
        Assert.Contains("Code button", result.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RequiresValidRemoteUrl_RejectsATokenBuiltIntoTheAddress()
    {
        // A form many beginner tutorials still recommend. It would work, but it writes the
        // token to .git/config, the command log, and the clipboard — this app never needs it.
        var result = new RequiresValidRemoteUrl()
            .Evaluate(State(hasRemote: false), UrlRequest("https://ghp_xxx@github.com/me/project.git"));

        Assert.False(result.Satisfied);
        Assert.Contains("token", result.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RequiresValidRemoteUrl_RejectsAnAddressThatStopsAtTheHost()
    {
        // Too few segments is just as broken as too many: there is no owner or project here.
        var result = new RequiresValidRemoteUrl()
            .Evaluate(State(hasRemote: false), UrlRequest("https://github.com"));

        Assert.False(result.Satisfied);
        Assert.Contains("Code button", result.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RequiresValidRemoteUrl_AsksForAnAddressWhenTheBoxIsEmpty(string? url)
    {
        var result = new RequiresValidRemoteUrl().Evaluate(State(hasRemote: false), UrlRequest(url));

        Assert.False(result.Satisfied);
        Assert.Contains("paste", result.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RequiresValidRemoteUrl_RejectsSomethingThatIsNotAnAddressAtAll()
    {
        var result = new RequiresValidRemoteUrl().Evaluate(State(hasRemote: false), UrlRequest("my-project"));

        Assert.False(result.Satisfied);
        Assert.Contains("https://", result.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public void RequiresValidRemoteUrl_RejectsWhitespaceInAnSshAddressToo()
    {
        // The git@ form is checked by the same rule as the https:// one — a stray space
        // is a paste mistake either way.
        var result = new RequiresValidRemoteUrl()
            .Evaluate(State(hasRemote: false), UrlRequest("git@github.com: me/project.git"));

        Assert.False(result.Satisfied);
        Assert.Contains("space", result.Message!, StringComparison.OrdinalIgnoreCase);
    }
}
