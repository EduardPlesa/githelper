using GitHelper.App.ViewModels;
using GitHelper.Core.Git;
using GitHelper.Core.Model;
using GitHelper.Core.Repo;

namespace GitHelper.App.Tests;

public class DiffViewModelTests
{
    private sealed class FakeSource : IDiffSource
    {
        public Func<FileDiff>? Next { get; set; }
        public int Reads { get; private set; }

        /// <summary>The original path the last read was given, so a test can assert it arrived.</summary>
        public string? LastOriginalPath { get; private set; }

        public Task<FileDiff> ReadAsync(
            string repoPath, string path, DiffSide side, string? originalPath, CancellationToken ct)
        {
            Reads++;
            LastOriginalPath = originalPath;
            return Task.FromResult(Next!());
        }
    }

    private static FileDiff TextDiff(string path = "a.txt") => new(
        path,
        DiffKind.Text,
        new[]
        {
            new DiffHunk("@@ -1,1 +1,1 @@", 1, 1, 1, 1, new[]
            {
                new DiffLine(DiffLineKind.Added, "hello", null, 1),
            }),
        },
        Truncated: false);

    private static FileDiff Of(DiffKind kind, bool truncated = false) =>
        new("a.txt", kind, Array.Empty<DiffHunk>(), truncated);

    /// <summary>
    /// The intro is authored content, not a paragraph typed into the view. Available before
    /// anything is opened, because it explains the format rather than the file.
    /// </summary>
    [Fact]
    public void TakesItsIntroFromTheContentLibrary()
    {
        var viewModel = new DiffViewModel(new FakeSource(), TestContent.Library);

        Assert.Equal(TestContent.Library.Reading["file-changes"].What, viewModel.IntroBlocks);
        Assert.NotEmpty(viewModel.IntroBlocks);
    }

    [Fact]
    public async Task PublishesHunksAndNamesTheSideOnOpen()
    {
        var source = new FakeSource { Next = () => TextDiff() };
        var viewModel = new DiffViewModel(source, TestContent.Library);

        await viewModel.OpenAsync("repo", "a.txt", DiffSide.Unstaged, renamedFrom: null, default);

        Assert.Equal("a.txt", viewModel.Path);
        Assert.Equal("Changes you have not staged yet", viewModel.SideLabel);
        Assert.Single(viewModel.Hunks);
        Assert.True(viewModel.HasHunks);
        Assert.False(viewModel.HasMessage);
    }

    [Fact]
    public async Task NamesTheStagedSideDifferently()
    {
        var source = new FakeSource { Next = () => TextDiff() };
        var viewModel = new DiffViewModel(source, TestContent.Library);

        await viewModel.OpenAsync("repo", "a.txt", DiffSide.Staged, renamedFrom: null, default);

        Assert.Equal("Changes you have staged, ready to commit", viewModel.SideLabel);
    }

    /// <summary>
    /// An untracked file has no committed version to compare against, so every line in it is
    /// an addition. Calling that "changes you have not staged yet" would contradict the row
    /// that opened this surface, which labels the same file "new file".
    /// </summary>
    [Fact]
    public async Task NamesAnUntrackedFileAsNewRatherThanUnstaged()
    {
        var source = new FakeSource { Next = () => TextDiff() };
        var viewModel = new DiffViewModel(source, TestContent.Library);

        await viewModel.OpenAsync("repo", "new.txt", DiffSide.Untracked, renamedFrom: null, default);

        Assert.Equal("Every line in this new file", viewModel.SideLabel);
    }

    [Fact]
    public async Task RefreshReReadsTheSameFile()
    {
        var source = new FakeSource { Next = () => TextDiff() };
        var viewModel = new DiffViewModel(source, TestContent.Library);
        await viewModel.OpenAsync("repo", "a.txt", DiffSide.Unstaged, renamedFrom: null, default);

        await viewModel.RefreshAsync(default);

        Assert.Equal(2, source.Reads);
    }

    /// <summary>
    /// Publishing a blank surface would tell the user their changes had vanished. The same
    /// rule RefreshAsync already keeps for the repository snapshot.
    /// </summary>
    [Fact]
    public async Task AFailedRefreshKeepsThePreviousDiffAndReports()
    {
        var source = new FakeSource { Next = () => TextDiff() };
        var viewModel = new DiffViewModel(source, TestContent.Library);
        await viewModel.OpenAsync("repo", "a.txt", DiffSide.Unstaged, renamedFrom: null, default);

        source.Next = () => throw new GitReadException(
            new GitCommandResult(
                new[] { "diff" }, string.Empty, "fatal: bad thing", 128, TimeSpan.Zero));
        await viewModel.RefreshAsync(default);

        Assert.Single(viewModel.Hunks);
        Assert.True(viewModel.HasMessage);
        Assert.Contains("could not", viewModel.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaysWhichSideIsEmptyRatherThanSayingNoChanges()
    {
        var source = new FakeSource { Next = () => Of(DiffKind.Empty) };
        var viewModel = new DiffViewModel(source, TestContent.Library);

        await viewModel.OpenAsync("repo", "a.txt", DiffSide.Staged, renamedFrom: null, default);

        Assert.Empty(viewModel.Hunks);
        Assert.Equal("There are no staged changes in this file any more.", viewModel.Message);
    }

    /// <summary>
    /// A staged rename with no edits parses as Empty, and "no changes" would be a flat lie
    /// about a file that is plainly staged and plainly renamed. The snapshot knows; the diff
    /// genuinely does not carry it.
    /// </summary>
    [Fact]
    public async Task ExplainsARenameWithNoContentChange()
    {
        var source = new FakeSource { Next = () => Of(DiffKind.Empty) };
        var viewModel = new DiffViewModel(source, TestContent.Library);

        await viewModel.OpenAsync("repo", "new.txt", DiffSide.Staged, renamedFrom: "old.txt", default);

        Assert.Equal(
            "This file was renamed from old.txt. Its contents are unchanged.",
            viewModel.Message);
    }

    /// <summary>
    /// The old name is not just narration: it has to reach the pathspec, or git reports a
    /// staged rename as a wholly new file and the sentence below never gets a chance to run.
    /// </summary>
    [Fact]
    public async Task PassesTheOldNameToTheReaderSoGitCanSeeTheRename()
    {
        var source = new FakeSource { Next = () => Of(DiffKind.Empty) };
        var viewModel = new DiffViewModel(source, TestContent.Library);

        await viewModel.OpenAsync("repo", "new.txt", DiffSide.Staged, renamedFrom: "old.txt", default);

        Assert.Equal("old.txt", source.LastOriginalPath);
    }

    /// <summary>
    /// The other half of the pair. A file can hold staged and unstaged changes at once, so
    /// each sentence has to name its own side — "no changes" would be ambiguous exactly
    /// when the user most needs to know which.
    /// </summary>
    [Fact]
    public async Task SaysTheUnstagedSideIsEmptyInItsOwnWords()
    {
        var source = new FakeSource { Next = () => Of(DiffKind.Empty) };
        var viewModel = new DiffViewModel(source, TestContent.Library);

        await viewModel.OpenAsync("repo", "a.txt", DiffSide.Unstaged, renamedFrom: null, default);

        Assert.Empty(viewModel.Hunks);
        Assert.Equal("There are no unstaged changes in this file any more.", viewModel.Message);
    }

    [Fact]
    public async Task SaysAnUntrackedFileStoppedBeingNewInItsOwnWords()
    {
        var source = new FakeSource { Next = () => Of(DiffKind.Empty) };
        var viewModel = new DiffViewModel(source, TestContent.Library);

        await viewModel.OpenAsync("repo", "new.txt", DiffSide.Untracked, renamedFrom: null, default);

        Assert.Equal("This file is no longer new to git.", viewModel.Message);
    }

    [Fact]
    public async Task ExplainsABinaryFile()
    {
        var source = new FakeSource { Next = () => Of(DiffKind.Binary) };
        var viewModel = new DiffViewModel(source, TestContent.Library);

        await viewModel.OpenAsync("repo", "logo.png", DiffSide.Unstaged, renamedFrom: null, default);

        Assert.Contains("not a text file", viewModel.Message);
    }

    [Fact]
    public async Task NamesTheRealCommandWhenTheDiffIsCutOff()
    {
        var source = new FakeSource { Next = () => TextDiff() with { Truncated = true } };
        var viewModel = new DiffViewModel(source, TestContent.Library);

        await viewModel.OpenAsync("repo", "a.txt", DiffSide.Unstaged, renamedFrom: null, default);

        Assert.True(viewModel.HasHunks);
        Assert.Contains("git diff -- a.txt", viewModel.Message);
    }

    /// <summary>
    /// A plain `git diff` shows the unstaged side. Offering it for a truncated staged diff
    /// sends the reader to a different file state entirely — and this app's whole premise is
    /// that a command it prints is one you can paste into a terminal.
    /// </summary>
    [Fact]
    public async Task NamesTheStagedCommandWhenAStagedDiffIsCutOff()
    {
        var source = new FakeSource { Next = () => TextDiff() with { Truncated = true } };
        var viewModel = new DiffViewModel(source, TestContent.Library);

        await viewModel.OpenAsync("repo", "a.txt", DiffSide.Staged, renamedFrom: null, default);

        Assert.Contains("git diff --cached -- a.txt", viewModel.Message);
    }

    /// <summary>
    /// And for a file git has never seen, a plain `git diff` prints nothing whatsoever.
    /// </summary>
    [Fact]
    public async Task NamesTheNoIndexCommandWhenAnUntrackedDiffIsCutOff()
    {
        var source = new FakeSource { Next = () => TextDiff() with { Truncated = true } };
        var viewModel = new DiffViewModel(source, TestContent.Library);

        await viewModel.OpenAsync("repo", "new.txt", DiffSide.Untracked, renamedFrom: null, default);

        Assert.Contains("git diff --no-index -- /dev/null new.txt", viewModel.Message);
    }

    [Fact]
    public async Task ClosingRaisesTheCallback()
    {
        var closed = false;
        var viewModel = new DiffViewModel(new FakeSource { Next = () => TextDiff() }, TestContent.Library)
        {
            CloseRequested = () => closed = true,
        };

        viewModel.CloseCommand.Execute(null);

        Assert.True(closed);
    }
}
