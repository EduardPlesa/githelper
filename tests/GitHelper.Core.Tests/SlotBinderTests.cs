using GitHelper.Core.Content;
using GitHelper.Core.Model;

namespace GitHelper.Core.Tests;

public class SlotBinderTests
{
    private static RepoState State(
        string? branch = "main",
        string? upstream = "origin/main",
        int ahead = 0,
        int behind = 0,
        params FileChange[] changes)
        => new(
            RepoRoot: @"C:\repos\demo",
            Branch: branch,
            IsDetached: branch is null,
            Upstream: upstream,
            Ahead: ahead,
            Behind: behind,
            HasCommits: true,
            HasRemote: upstream is not null,
            Changes: changes,
            RecentCommits: Array.Empty<CommitInfo>(),
            Branches: Array.Empty<BranchInfo>(),
            Tags: Array.Empty<TagInfo>(),
            Stashes: Array.Empty<StashInfo>());

    [Fact]
    public void Bind_ProvidesBranchAndUpstream()
    {
        var values = SlotBinder.Bind(State());

        Assert.Equal("main", values["branch"]);
        Assert.Equal("origin/main", values["upstream"]);
    }

    [Fact]
    public void Bind_CountsStagedUnstagedAndUntrackedSeparately()
    {
        var values = SlotBinder.Bind(State(changes: new[]
        {
            new FileChange("a.txt", null, ChangeKind.Modified, ChangeKind.None),
            new FileChange("b.txt", null, ChangeKind.None, ChangeKind.Modified),
            new FileChange("c.txt", null, ChangeKind.None, ChangeKind.Untracked),
        }));

        Assert.Equal("1", values["stagedCount"]);
        Assert.Equal("1", values["unstagedCount"]);
        Assert.Equal("1", values["untrackedCount"]);
    }

    [Fact]
    public void Bind_DescribesDetachedHeadAndMissingUpstreamInPlainWords()
    {
        var values = SlotBinder.Bind(State(branch: null, upstream: null));

        Assert.Equal("no branch (detached)", values["branch"]);
        Assert.Equal("no upstream branch", values["upstream"]);
    }

    [Fact]
    public void Bind_IncludesRequestValues()
    {
        var values = SlotBinder.Bind(State(), path: "src/app.cs", branchName: "feature");

        Assert.Equal("src/app.cs", values["path"]);
        Assert.Equal("feature", values["branchName"]);
    }

    [Fact]
    public void Bind_IncludesTagName()
    {
        var values = SlotBinder.Bind(State(), tagName: "v1");

        Assert.Equal("v1", values["tagName"]);
    }

    [Fact]
    public void Bind_DescribesAMissingTagNamePlainly()
    {
        Assert.Equal("the tag", SlotBinder.Bind(State())["tagName"]);
    }

    [Fact]
    public void Bind_ListsStagedFilesAndTruncatesLongLists()
    {
        var many = Enumerable.Range(1, 10)
            .Select(i => new FileChange($"f{i}.txt", null, ChangeKind.Modified, ChangeKind.None))
            .ToArray();

        var values = SlotBinder.Bind(State(changes: many));

        Assert.Contains("f1.txt", values["stagedFileList"]);
        Assert.Contains("and 7 more", values["stagedFileList"]);
    }

    [Fact]
    public void KnownSlots_CoversEverySlotBindProduces()
    {
        var values = SlotBinder.Bind(State(), path: "p", branchName: "b");

        Assert.Equal(SlotBinder.KnownSlots.OrderBy(s => s), values.Keys.OrderBy(s => s));
    }

    [Fact]
    public void RemoteUrlIsBoundWhenSuppliedAndDescribedWhenNot()
    {
        Assert.Equal(
            "https://github.com/me/p.git",
            SlotBinder.Bind(State(), remoteUrl: "https://github.com/me/p.git")["remoteUrl"]);

        Assert.Equal("the address you paste", SlotBinder.Bind(State())["remoteUrl"]);
    }
}
