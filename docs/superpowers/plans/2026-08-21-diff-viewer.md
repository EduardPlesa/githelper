# Diff Viewer (v2.5) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show a single file's unstaged or staged changes as the unified diff git itself prints, reached from a button on each row of the Changes tab.

**Architecture:** A new `DiffReader` in Core reads one file's diff on demand — deliberately not part of `RepoState`, which is a whole-repo snapshot re-read on every watcher tick. `DiffParser` turns git's output into a closed model (`FileDiff` / `DiffHunk` / `DiffLine`) that carries every line's old and new number. A new `DiffViewModel` is swapped into the middle column in place of the current tab, and re-read inside the existing `RefreshAsync` so what is on screen stays observed rather than remembered.

**Tech Stack:** C# / .NET 10, Avalonia (MVVM via CommunityToolkit.Mvvm), xUnit, YamlDotNet for content frontmatter. No new dependencies.

**Spec:** `docs/superpowers/specs/2026-08-21-diff-viewer-design.md`

## Global Constraints

- `TreatWarningsAsErrors` is `true` in every project — an unused `using` fails the build.
- Every `GitRunner` invocation is an argv array, never a constructed string. Never introduce stdin or shell-string building.
- `--` always precedes a path in argv. Without it a file named like a ref makes git guess.
- Content file id equals its file name. `.md` files are picked up by wildcard from the folders listed in `GitHelper.Content.csproj` — a **new folder must be added to that csproj** or its files are silently absent at runtime.
- `ContentParserTests.ShippedContent_NeverLeavesLiteralAsterisksInProse` is real: the parser has no emphasis beyond `**strong**`, so `*like this*` ships as literal asterisks and fails the suite. Write plain prose.
- Every glossary term must be referenced by something (`EveryGlossaryTermIsActuallyReferencedSomewhere`).
- The app has **29 actions** before and after this plan — nothing here is an action. Terms go **16 to 18**. Do not write a count you have not counted.
- Suite is **635 tests** (346 Core + 289 App) at the start. Run `dotnet test GitHelper.sln` from the repo root before each commit.
- Conflicted (unmerged) files are **out of scope**. `git diff` emits combined diff format for them (`@@@ -1,4 -1,4 +1,6 @@@`), a second grammar this parser does not read. No View button on conflicted rows.

**Verified before this plan was written, on Windows, against a real repository** — do not re-litigate:

```
$ git diff --no-color --no-ext-diff --no-index -- /dev/null new.txt
diff --git a/new.txt b/new.txt
new file mode 100644
index 0000000..fbbee86
--- /dev/null
+++ b/new.txt
@@ -0,0 +1,2 @@
+alpha
+beta
exit=1
```

Three facts follow from it. `/dev/null` is the correct spelling on Windows — no temp file needed. The exit code is **1 on success**, so this read must never be judged by `Success`. And the hunk header is `@@ -0,0 ...` — a zero old-start, which the parser must accept.

---

### Task 1: The diff model and `DiffParser`

Pure parsing, no repository needed. Every awkward input is cheap to cover here, which is why this task comes first.

**Files:**
- Create: `src/GitHelper.Core/Model/FileDiff.cs`
- Create: `src/GitHelper.Core/Parsing/DiffParser.cs`
- Test: `tests/GitHelper.Core.Tests/DiffParserTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `FileDiff(string Path, DiffKind Kind, IReadOnlyList<DiffHunk> Hunks, bool Truncated)`; `DiffHunk(string Header, int OldStart, int OldCount, int NewStart, int NewCount, IReadOnlyList<DiffLine> Lines)`; `DiffLine(DiffLineKind Kind, string Text, int? OldLineNumber, int? NewLineNumber)`; enums `DiffKind { Text, Binary, Empty }` and `DiffLineKind { Context, Added, Removed, NoNewlineMarker }`; `DiffParser.Parse(string path, string output)` and `DiffParser.MaxLines`.

- [ ] **Step 1: Write the failing tests**

Create `tests/GitHelper.Core.Tests/DiffParserTests.cs`:

```csharp
using GitHelper.Core.Model;
using GitHelper.Core.Parsing;

namespace GitHelper.Core.Tests;

public class DiffParserTests
{
    private const string ModifiedFile =
        "diff --git a/a.txt b/a.txt\n" +
        "index 1234567..89abcde 100644\n" +
        "--- a/a.txt\n" +
        "+++ b/a.txt\n" +
        "@@ -1,3 +1,4 @@\n" +
        " one\n" +
        "-two\n" +
        "+TWO\n" +
        "+two and a half\n" +
        " three\n";

    [Fact]
    public void ReadsHunkCountsAndLineKinds()
    {
        var diff = DiffParser.Parse("a.txt", ModifiedFile);

        Assert.Equal(DiffKind.Text, diff.Kind);
        Assert.False(diff.Truncated);
        var hunk = Assert.Single(diff.Hunks);
        Assert.Equal("@@ -1,3 +1,4 @@", hunk.Header);
        Assert.Equal(1, hunk.OldStart);
        Assert.Equal(3, hunk.OldCount);
        Assert.Equal(1, hunk.NewStart);
        Assert.Equal(4, hunk.NewCount);

        Assert.Equal(
            new[]
            {
                DiffLineKind.Context, DiffLineKind.Removed, DiffLineKind.Added,
                DiffLineKind.Added, DiffLineKind.Context,
            },
            hunk.Lines.Select(l => l.Kind));
    }

    /// <summary>
    /// The numbers are what v3 will address lines by, and they are the part a reader cannot
    /// eyeball. A removed line has no line in the new file, and an added line none in the old.
    /// </summary>
    [Fact]
    public void NumbersEveryLineOnTheSideItExistsOn()
    {
        var lines = DiffParser.Parse("a.txt", ModifiedFile).Hunks.Single().Lines;

        Assert.Equal(new int?[] { 1, 2, null, null, 3 }, lines.Select(l => l.OldLineNumber));
        Assert.Equal(new int?[] { 1, null, 2, 3, 4 }, lines.Select(l => l.NewLineNumber));
    }

    /// <summary>Omitted counts mean 1. Requiring the comma silently drops single-line hunks.</summary>
    [Fact]
    public void AcceptsAHunkHeaderWithTheCountsOmitted()
    {
        var text =
            "diff --git a/a.txt b/a.txt\n" +
            "--- a/a.txt\n" +
            "+++ b/a.txt\n" +
            "@@ -1 +1 @@\n" +
            "-old\n" +
            "+new\n";

        var hunk = Assert.Single(DiffParser.Parse("a.txt", text).Hunks);

        Assert.Equal(1, hunk.OldCount);
        Assert.Equal(1, hunk.NewCount);
    }

    /// <summary>The shape a new file arrives in through --no-index. Zero old-start is legal.</summary>
    [Fact]
    public void AcceptsAZeroOldStartForAWhollyNewFile()
    {
        var text =
            "diff --git a/new.txt b/new.txt\n" +
            "new file mode 100644\n" +
            "index 0000000..fbbee86\n" +
            "--- /dev/null\n" +
            "+++ b/new.txt\n" +
            "@@ -0,0 +1,2 @@\n" +
            "+alpha\n" +
            "+beta\n";

        var hunk = Assert.Single(DiffParser.Parse("new.txt", text).Hunks);

        Assert.Equal(0, hunk.OldStart);
        Assert.Equal(0, hunk.OldCount);
        Assert.All(hunk.Lines, l => Assert.Equal(DiffLineKind.Added, l.Kind));
        Assert.Equal(new int?[] { 1, 2 }, hunk.Lines.Select(l => l.NewLineNumber));
    }

    /// <summary>
    /// The header's own --- and +++ lines would read as a removed and an added line under a
    /// colour-by-first-character rule. This is the single strongest reason a parser exists.
    /// </summary>
    [Fact]
    public void DoesNotMistakeFileHeadersForChangedLines()
    {
        var lines = DiffParser.Parse("a.txt", ModifiedFile).Hunks.Single().Lines;

        Assert.DoesNotContain(lines, l => l.Text is "-- a/a.txt" or "++ b/a.txt");
        Assert.Equal(5, lines.Count);
    }

    [Fact]
    public void ReadsTheNoNewlineMarkerAsItsOwnKind()
    {
        var text =
            "diff --git a/a.txt b/a.txt\n" +
            "--- a/a.txt\n" +
            "+++ b/a.txt\n" +
            "@@ -1 +1 @@\n" +
            "-old\n" +
            "+new\n" +
            "\\ No newline at end of file\n";

        var last = DiffParser.Parse("a.txt", text).Hunks.Single().Lines.Last();

        Assert.Equal(DiffLineKind.NoNewlineMarker, last.Kind);
        Assert.Null(last.OldLineNumber);
        Assert.Null(last.NewLineNumber);
    }

    [Theory]
    [InlineData("diff --git a/x.png b/x.png\nBinary files a/x.png and b/x.png differ\n")]
    [InlineData("diff --git a/x.png b/x.png\nGIT binary patch\nliteral 12\n")]
    public void ReportsBinaryFilesAsBinary(string text)
    {
        var diff = DiffParser.Parse("x.png", text);

        Assert.Equal(DiffKind.Binary, diff.Kind);
        Assert.Empty(diff.Hunks);
    }

    /// <summary>Legitimate: a file edited and reverted between the snapshot and the read.</summary>
    [Fact]
    public void ReportsEmptyOutputAsEmptyRatherThanFailing()
    {
        Assert.Equal(DiffKind.Empty, DiffParser.Parse("a.txt", string.Empty).Kind);
    }

    /// <summary>A staged rename with no edits: a header, and no hunks at all.</summary>
    [Fact]
    public void ReportsARenameWithNoContentChangeAsEmpty()
    {
        var text =
            "diff --git a/old.txt b/new.txt\n" +
            "similarity index 100%\n" +
            "rename from old.txt\n" +
            "rename to new.txt\n";

        Assert.Equal(DiffKind.Empty, DiffParser.Parse("new.txt", text).Kind);
    }

    [Fact]
    public void ReadsSeveralHunksFromOneFile()
    {
        var text =
            "diff --git a/a.txt b/a.txt\n" +
            "--- a/a.txt\n" +
            "+++ b/a.txt\n" +
            "@@ -1,2 +1,2 @@\n" +
            "-one\n" +
            "+ONE\n" +
            " two\n" +
            "@@ -10,2 +10,2 @@ some section heading\n" +
            "-ten\n" +
            "+TEN\n" +
            " eleven\n";

        var diff = DiffParser.Parse("a.txt", text);

        Assert.Equal(2, diff.Hunks.Count);
        Assert.Equal(10, diff.Hunks[1].OldStart);
        Assert.Equal("@@ -10,2 +10,2 @@ some section heading", diff.Hunks[1].Header);
    }

    [Fact]
    public void StopsAtTheLineCapAndSaysSo()
    {
        var body = string.Concat(Enumerable.Repeat("+line\n", DiffParser.MaxLines + 50));
        var text =
            "diff --git a/a.txt b/a.txt\n" +
            "--- a/a.txt\n" +
            "+++ b/a.txt\n" +
            $"@@ -0,0 +1,{DiffParser.MaxLines + 50} @@\n" +
            body;

        var diff = DiffParser.Parse("a.txt", text);

        Assert.True(diff.Truncated);
        Assert.Equal(DiffParser.MaxLines, diff.Hunks.Sum(h => h.Lines.Count));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GitHelper.Core.Tests --filter DiffParserTests`
Expected: FAIL — the build cannot find `FileDiff`, `DiffParser`, or their members.

- [ ] **Step 3: Write the model**

Create `src/GitHelper.Core/Model/FileDiff.cs`:

```csharp
namespace GitHelper.Core.Model;

/// <summary>
/// One file's changes, as git printed them. Read on demand and deliberately not part of
/// <see cref="RepoState"/>: that snapshot is re-read on every watcher tick and must stay
/// cheap, and a diff has no bound on its size.
/// </summary>
public sealed record FileDiff(
    string Path,
    DiffKind Kind,
    IReadOnlyList<DiffHunk> Hunks,
    /// <summary>
    /// Only ever meaningful for <see cref="DiffKind.Text"/>. A separate flag rather than a
    /// fourth DiffKind, because truncation is not an alternative to being binary.
    /// </summary>
    bool Truncated);

public enum DiffKind
{
    Text,
    Binary,

    /// <summary>No differences. Not an error: a file can be edited and reverted between
    /// the snapshot being taken and the diff being read.</summary>
    Empty,
}

/// <summary>
/// One run of changed lines and the context around it. <paramref name="Header"/> is kept
/// verbatim as well as parsed, because the UI shows it and underlines it as jargon rather
/// than hiding it.
/// </summary>
public sealed record DiffHunk(
    string Header,
    int OldStart,
    int OldCount,
    int NewStart,
    int NewCount,
    IReadOnlyList<DiffLine> Lines);

/// <summary>
/// One line. The numbers are null rather than zero on the side where the line does not
/// exist — the same reason RebaseProgress is null for a merge instead of carrying 0 of 0.
/// </summary>
public sealed record DiffLine(
    DiffLineKind Kind,
    string Text,
    int? OldLineNumber,
    int? NewLineNumber);

public enum DiffLineKind
{
    Context,
    Added,
    Removed,

    /// <summary>Git's own "\ No newline at end of file". Printed, not context, and not hidden:
    /// hiding it would make this app's diff disagree with the terminal's.</summary>
    NoNewlineMarker,
}
```

- [ ] **Step 4: Write the parser**

Create `src/GitHelper.Core/Parsing/DiffParser.cs`:

```csharp
using System.Text.RegularExpressions;
using GitHelper.Core.Model;

namespace GitHelper.Core.Parsing;

/// <summary>
/// Parses git's unified diff for a single file. Deliberately a parser rather than
/// colour-by-first-character: the header's own '---' and '+++' lines would read as a removed
/// and an added line under that rule, and nothing would carry line numbers.
/// </summary>
public static partial class DiffParser
{
    /// <summary>
    /// Where a diff stops being something to read and becomes something to grep. Named for
    /// the same reason RepoStateReader.RecentCommitLimit is, rather than buried in a method.
    /// </summary>
    public const int MaxLines = 2000;

    public static FileDiff Parse(string path, string output)
    {
        var empty = new FileDiff(path, DiffKind.Empty, Array.Empty<DiffHunk>(), Truncated: false);
        if (string.IsNullOrWhiteSpace(output)) return empty;

        var lines = output.Replace("\r\n", "\n").Split('\n');

        if (lines.Any(IsBinaryMarker))
            return new FileDiff(path, DiffKind.Binary, Array.Empty<DiffHunk>(), Truncated: false);

        var hunks = new List<DiffHunk>();
        var current = new List<DiffLine>();
        DiffHunk? header = null;
        var oldNumber = 0;
        var newNumber = 0;
        var emitted = 0;
        var truncated = false;

        void CloseHunk()
        {
            if (header is null) return;
            hunks.Add(header with { Lines = current.ToArray() });
            header = null;
            current = new List<DiffLine>();
        }

        foreach (var line in lines)
        {
            var match = HunkHeader().Match(line);
            if (match.Success)
            {
                CloseHunk();
                oldNumber = int.Parse(match.Groups["oldStart"].Value);
                newNumber = int.Parse(match.Groups["newStart"].Value);
                header = new DiffHunk(
                    Header: line,
                    OldStart: oldNumber,
                    OldCount: Count(match.Groups["oldCount"]),
                    NewStart: newNumber,
                    NewCount: Count(match.Groups["newCount"]),
                    Lines: Array.Empty<DiffLine>());
                continue;
            }

            // Everything before the first hunk header is file metadata: 'diff --git', 'index',
            // '--- a/x', '+++ b/x', mode and rename lines. None of it is a changed line.
            if (header is null) continue;

            if (emitted == MaxLines)
            {
                truncated = true;
                break;
            }

            if (line.Length == 0) continue;

            DiffLine parsed;
            switch (line[0])
            {
                case ' ':
                    parsed = new DiffLine(DiffLineKind.Context, line[1..], oldNumber++, newNumber++);
                    break;
                case '+':
                    parsed = new DiffLine(DiffLineKind.Added, line[1..], null, newNumber++);
                    break;
                case '-':
                    parsed = new DiffLine(DiffLineKind.Removed, line[1..], oldNumber++, null);
                    break;
                case '\\':
                    parsed = new DiffLine(DiffLineKind.NoNewlineMarker, line[2..], null, null);
                    break;
                default:
                    // A line git's format does not define. Skipped rather than guessed at.
                    continue;
            }

            current.Add(parsed);
            emitted++;
        }

        CloseHunk();

        // No hunks at all is a real answer, not a parse failure: a rename with no edits
        // produces headers and nothing else.
        return hunks.Count == 0
            ? empty
            : new FileDiff(path, DiffKind.Text, hunks, truncated);
    }

    private static int Count(Group group)
        => group.Success ? int.Parse(group.Value) : 1;

    private static bool IsBinaryMarker(string line)
        => (line.StartsWith("Binary files ", StringComparison.Ordinal)
                && line.EndsWith(" differ", StringComparison.Ordinal))
            || line.StartsWith("GIT binary patch", StringComparison.Ordinal);

    // Counts are optional: git writes "@@ -1 +1 @@" when a hunk is one line long.
    [GeneratedRegex(@"^@@ -(?<oldStart>\d+)(,(?<oldCount>\d+))? \+(?<newStart>\d+)(,(?<newCount>\d+))? @@")]
    private static partial Regex HunkHeader();
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/GitHelper.Core.Tests --filter DiffParserTests`
Expected: PASS, 11 tests.

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test GitHelper.sln`
Expected: PASS, 646 tests (635 + 11).

- [ ] **Step 7: Commit**

```bash
git add src/GitHelper.Core/Model/FileDiff.cs src/GitHelper.Core/Parsing/DiffParser.cs tests/GitHelper.Core.Tests/DiffParserTests.cs
git commit -m "feat: parse a unified diff into hunks and numbered lines"
```

---

### Task 2: `DiffReader`

Reads one file's diff from a real repository. The regression this task exists to prevent is judging `--no-index` by its exit code.

**Files:**
- Create: `src/GitHelper.Core/Repo/DiffReader.cs`
- Modify: `tests/GitHelper.TestSupport/TestRepo.cs` (add two helpers at the end of the class, before `Dispose`)
- Test: `tests/GitHelper.Core.Tests/DiffReaderTests.cs`

**Interfaces:**
- Consumes: `DiffParser.Parse`, `FileDiff`, `DiffKind` from Task 1; `IGitRunner`, `GitReadException`.
- Produces: `enum DiffSide { Unstaged, Staged, Untracked }`; `DiffReader(IGitRunner runner)` with `Task<FileDiff> ReadAsync(string repoPath, string path, DiffSide side, CancellationToken ct = default)`.

- [ ] **Step 1: Add the test-repository helpers**

In `tests/GitHelper.TestSupport/TestRepo.cs`, add before `public void Dispose()`:

```csharp
    /// <summary>Commits a file, then edits it again without staging: one unstaged change.</summary>
    public async Task<string> AddUnstagedChangeAsync(string relativePath = "tracked.txt")
    {
        WriteFile(relativePath, "one\ntwo\nthree\n");
        await GitAsync("add", "-A");
        await GitAsync("commit", "-q", "-m", "add " + relativePath);

        WriteFile(relativePath, "one\nTWO\nthree\n");
        return relativePath;
    }

    /// <summary>
    /// Leaves a file staged AND further modified, so the same path has two different diffs.
    /// This is the case the two-rows-one-file UI depends on.
    /// </summary>
    public async Task<string> AddStagedAndFurtherModifiedAsync(string relativePath = "both.txt")
    {
        WriteFile(relativePath, "one\ntwo\n");
        await GitAsync("add", "-A");
        await GitAsync("commit", "-q", "-m", "add " + relativePath);

        WriteFile(relativePath, "one\nSTAGED\n");
        await GitAsync("add", relativePath);
        WriteFile(relativePath, "one\nWORKTREE\n");
        return relativePath;
    }
```

- [ ] **Step 2: Write the failing tests**

Create `tests/GitHelper.Core.Tests/DiffReaderTests.cs`:

```csharp
using GitHelper.Core.Git;
using GitHelper.Core.Model;
using GitHelper.Core.Repo;
using GitHelper.TestSupport;

namespace GitHelper.Core.Tests;

public class DiffReaderTests
{
    private static DiffReader Reader() => new(new GitRunner());

    [Fact]
    public async Task ReadsAnUnstagedChange()
    {
        using var repo = await TestRepo.CreateAsync();
        var path = await repo.AddUnstagedChangeAsync();

        var diff = await Reader().ReadAsync(repo.Path, path, DiffSide.Unstaged);

        Assert.Equal(DiffKind.Text, diff.Kind);
        Assert.Contains(
            diff.Hunks.SelectMany(h => h.Lines),
            l => l.Kind == DiffLineKind.Added && l.Text == "TWO");
    }

    [Fact]
    public async Task ReadsAStagedChange()
    {
        using var repo = await TestRepo.CreateAsync();
        var path = await repo.AddStagedAndFurtherModifiedAsync();

        var diff = await Reader().ReadAsync(repo.Path, path, DiffSide.Staged);

        Assert.Contains(
            diff.Hunks.SelectMany(h => h.Lines),
            l => l.Kind == DiffLineKind.Added && l.Text == "STAGED");
    }

    /// <summary>
    /// One path, two answers. The staged diff is index against HEAD and the unstaged one is
    /// worktree against index, so a file staged and then edited again must not report the
    /// same thing twice.
    /// </summary>
    [Fact]
    public async Task GivesTwoDifferentDiffsForOnePathWhenStagedAndFurtherModified()
    {
        using var repo = await TestRepo.CreateAsync();
        var path = await repo.AddStagedAndFurtherModifiedAsync();
        var reader = Reader();

        var staged = await reader.ReadAsync(repo.Path, path, DiffSide.Staged);
        var unstaged = await reader.ReadAsync(repo.Path, path, DiffSide.Unstaged);

        Assert.Contains(staged.Hunks.SelectMany(h => h.Lines), l => l.Text == "STAGED");
        Assert.Contains(unstaged.Hunks.SelectMany(h => h.Lines), l => l.Text == "WORKTREE");
        Assert.DoesNotContain(unstaged.Hunks.SelectMany(h => h.Lines), l => l.Text == "STAGED");
    }

    /// <summary>
    /// git diff --no-index exits 1 whenever the files differ, which is every untracked file.
    /// Judging this read by Success would report every new file as a failure — the same trap
    /// ActionOutcome.Paused hit at rebase.
    /// </summary>
    [Fact]
    public async Task ReadsAnUntrackedFileAsAllAddedDespiteANonZeroExit()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("new.txt", "alpha\nbeta\n");

        var diff = await Reader().ReadAsync(repo.Path, "new.txt", DiffSide.Untracked);

        Assert.Equal(DiffKind.Text, diff.Kind);
        var hunk = Assert.Single(diff.Hunks);
        Assert.Equal(0, hunk.OldStart);
        Assert.All(hunk.Lines, l => Assert.Equal(DiffLineKind.Added, l.Kind));
        Assert.Equal(new[] { "alpha", "beta" }, hunk.Lines.Select(l => l.Text));
    }

    [Fact]
    public async Task ReportsAFileWithNoChangesAsEmpty()
    {
        using var repo = await TestRepo.CreateAsync();

        var diff = await Reader().ReadAsync(repo.Path, "README.md", DiffSide.Unstaged);

        Assert.Equal(DiffKind.Empty, diff.Kind);
    }

    /// <summary>
    /// A read that genuinely failed — no output and a non-zero exit — must not be reported as
    /// an empty diff, which would claim the file is unchanged. A file that has gone from disk
    /// is the ordinary way to reach this: --no-index has nothing to open.
    /// </summary>
    [Fact]
    public async Task ThrowsWhenTheFileIsNotThereToRead()
    {
        using var repo = await TestRepo.CreateAsync();

        await Assert.ThrowsAsync<GitReadException>(
            () => Reader().ReadAsync(repo.Path, "never-existed.txt", DiffSide.Untracked));
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/GitHelper.Core.Tests --filter DiffReaderTests`
Expected: FAIL — `DiffReader` and `DiffSide` do not exist.

- [ ] **Step 4: Write the reader**

Create `src/GitHelper.Core/Repo/DiffReader.cs`:

```csharp
using GitHelper.Core.Git;
using GitHelper.Core.Model;
using GitHelper.Core.Parsing;

namespace GitHelper.Core.Repo;

/// <summary>Which comparison the user is looking at.</summary>
public enum DiffSide
{
    /// <summary>Worktree against index: what is changed but not chosen yet.</summary>
    Unstaged,

    /// <summary>Index against HEAD: what is chosen for the next commit.</summary>
    Staged,

    /// <summary>A file git has never seen, compared against nothing.</summary>
    Untracked,
}

/// <summary>
/// Reads one file's diff, on demand. A sibling of <see cref="RepoStateReader"/> and
/// deliberately not part of it: RepoState is a whole-repo snapshot re-read on every action
/// and every file-watcher tick, and diffs have no bound on their size.
/// </summary>
public sealed class DiffReader(IGitRunner runner)
{
    public async Task<FileDiff> ReadAsync(
        string repoPath,
        string path,
        DiffSide side,
        CancellationToken ct = default)
    {
        var result = await runner.RunAsync(repoPath, ArgsFor(path, side), ct);

        // Decided by the shape of the output, never by the exit code. --no-index exits 1
        // whenever the files differ, which is the normal case for every untracked file.
        if (result.StdOut.Length > 0) return DiffParser.Parse(path, result.StdOut);

        // Nothing to show and git was unhappy: a real failure. Reporting it as an empty diff
        // would tell the user their file is unchanged, which is the more dangerous lie.
        if (!result.Success) throw new GitReadException(result);

        return new FileDiff(path, DiffKind.Empty, Array.Empty<DiffHunk>(), Truncated: false);
    }

    private static string[] ArgsFor(string path, DiffSide side) => side switch
    {
        // --no-color because the parser reads text, not ANSI escapes. --no-ext-diff because a
        // configured difftool would return a format this parser has never seen. And '--'
        // always, or a file named like a ref makes git guess.
        DiffSide.Unstaged =>
            new[] { "diff", "--no-color", "--no-ext-diff", "--", path },
        DiffSide.Staged =>
            new[] { "diff", "--no-color", "--no-ext-diff", "--cached", "--", path },

        // /dev/null is git's own spelling for the empty side and is verified to work on
        // Windows; no temporary file is needed.
        DiffSide.Untracked =>
            new[] { "diff", "--no-color", "--no-ext-diff", "--no-index", "--", "/dev/null", path },

        _ => new[] { "diff", "--no-color", "--this-flag-does-not-exist" },
    };
}
```

Note on the default arm: an unknown `DiffSide` is a programming error, and this makes it fail loudly through the same `GitReadException` path as any other failed read rather than silently reading the wrong thing.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/GitHelper.Core.Tests --filter DiffReaderTests`
Expected: PASS, 6 tests.

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test GitHelper.sln`
Expected: PASS, 652 tests.

- [ ] **Step 7: Commit**

```bash
git add src/GitHelper.Core/Repo/DiffReader.cs tests/GitHelper.Core.Tests/DiffReaderTests.cs tests/GitHelper.TestSupport/TestRepo.cs
git commit -m "feat: read one file's staged, unstaged or untracked diff"
```

---

### Task 3: The `reading` content category and two terms

A read surface has no danger, no risks and nothing to undo, so it does not fit `ExplanationDocument`. This adds a fourth category rather than shipping three empty sections.

**Files:**
- Create: `src/GitHelper.Core/Content/ReadingDocument.cs`
- Modify: `src/GitHelper.Core/Content/ContentParser.cs` (add `ParseReading` after `Parse`)
- Modify: `src/GitHelper.Core/Content/ContentLibrary.cs` (add the `Reading` dictionary and its load branch)
- Modify: `src/GitHelper.Content/GitHelper.Content.csproj` (embed `reading/**/*.md`)
- Create: `src/GitHelper.Content/reading/file-changes.md`
- Create: `src/GitHelper.Content/terms/diff.md`
- Create: `src/GitHelper.Content/terms/hunk.md`
- Modify: `tests/GitHelper.Core.Tests/ContentIntegrityTests.cs` (walk `Reading`)

**Interfaces:**
- Consumes: `ContentBlock`, `ContentParser`, `ContentLibrary`.
- Produces: `ReadingDocument(string Id, string Title, IReadOnlyList<string> Terms, IReadOnlyList<ContentBlock> What)`; `ContentLibrary.Reading`; content ids `file-changes`, and term ids `diff` and `hunk`.

- [ ] **Step 1: Write the failing test**

Add to `tests/GitHelper.Core.Tests/ContentIntegrityTests.cs`, inside the class:

```csharp
    [Fact]
    public void ReadingDocumentsAreLoadedAndTheirTermsResolve()
    {
        Assert.NotEmpty(Library.Reading);

        var unresolved = Library.Reading.Values
            .SelectMany(d => d.Terms.Select(t => (Document: d.Id, Term: t)))
            .Where(x => !Library.Terms.ContainsKey(x.Term))
            .ToList();

        Assert.Empty(unresolved);
    }
```

And change `AllDocuments()` so the every-term-is-referenced rule sees the new category. Because `AllDocuments` returns `ExplanationDocument`, the reading terms are folded in at the reference site instead — replace the body of `EveryGlossaryTermIsActuallyReferencedSomewhere` with:

```csharp
    [Fact]
    public void EveryGlossaryTermIsActuallyReferencedSomewhere()
    {
        var referenced = AllDocuments()
            .SelectMany(d => d.Terms.Concat(AllSpans(d).OfType<TermSpan>().Select(s => s.TermId)))
            .Concat(Library.Reading.Values.SelectMany(d =>
                d.Terms.Concat(Spans(d.What).OfType<TermSpan>().Select(s => s.TermId))))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unused = Library.Terms.Keys.Where(id => !referenced.Contains(id)).ToList();

        Assert.Empty(unused);
    }
```

- [ ] **Step 2: Run it and watch it fail for the right reason**

Run: `dotnet test tests/GitHelper.Core.Tests --filter ContentIntegrityTests`
Expected: FAIL to build — `ContentLibrary.Reading` does not exist. That is the point: the rule is watched failing before it is satisfied, so it is known to be discriminating rather than vacuous.

- [ ] **Step 3: Add the document type**

Create `src/GitHelper.Core/Content/ReadingDocument.cs`:

```csharp
namespace GitHelper.Core.Content;

/// <summary>
/// Authored prose for a surface the user reads rather than an action they take.
///
/// Deliberately not an ExplanationDocument: a read surface has no danger level, no risks and
/// nothing to undo, and forcing it into that shape would ship three empty sections and a
/// 'danger' that describes nothing.
/// </summary>
public sealed record ReadingDocument(
    string Id,
    string Title,
    IReadOnlyList<string> Terms,
    IReadOnlyList<ContentBlock> What);
```

- [ ] **Step 4: Parse it**

In `src/GitHelper.Core/Content/ContentParser.cs`, add after the `Parse` method (it reuses the private `SplitFrontmatter` and `SplitSections` already in this class):

```csharp
    /// <summary>
    /// Parses a reading file: the same frontmatter, but one '## what' section and no danger.
    /// </summary>
    public static ReadingDocument ParseReading(string fileText, string sourceName)
    {
        var (frontmatterText, body) = SplitFrontmatter(fileText, sourceName);

        Frontmatter matter;
        try
        {
            matter = Yaml.Deserialize<Frontmatter>(frontmatterText) ?? new Frontmatter();
        }
        catch (Exception ex)
        {
            throw new ContentException($"{sourceName}: frontmatter is not valid YAML. {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(matter.Id))
            throw new ContentException($"{sourceName}: frontmatter is missing 'id'.");
        if (string.IsNullOrWhiteSpace(matter.Title))
            throw new ContentException($"{sourceName}: frontmatter is missing 'title'.");

        var sections = SplitSections(body, sourceName);
        if (!sections.TryGetValue("what", out var what))
            throw new ContentException($"{sourceName}: missing required section '## what'.");

        var blocks = ParseBlocks(what);
        if (blocks.Count == 0)
            throw new ContentException($"{sourceName}: '## what' section is empty.");

        return new ReadingDocument(
            Id: matter.Id!,
            Title: matter.Title!,
            Terms: matter.Terms ?? new List<string>(),
            What: blocks);
    }
```

- [ ] **Step 5: Load it**

In `src/GitHelper.Core/Content/ContentLibrary.cs`:

Add the property beside the other three:

```csharp
    public IReadOnlyDictionary<string, ReadingDocument> Reading { get; }
```

Add the constructor parameter and assignment, matching the existing style:

```csharp
    private ContentLibrary(
        IReadOnlyDictionary<string, ExplanationDocument> actions,
        IReadOnlyDictionary<string, ExplanationDocument> setup,
        IReadOnlyDictionary<string, ReadingDocument> reading,
        IReadOnlyDictionary<string, GlossaryTerm> terms)
    {
        Actions = actions;
        Setup = setup;
        Reading = reading;
        Terms = terms;
    }
```

In `Load`, add the dictionary beside the others:

```csharp
        var reading = new Dictionary<string, ReadingDocument>(StringComparer.OrdinalIgnoreCase);
```

Add the branch after the `.setup.` branch and before the `.terms.` one:

```csharp
            else if (resourceName.Contains(".reading.", StringComparison.OrdinalIgnoreCase))
            {
                var document = ContentParser.ParseReading(text, resourceName);
                if (reading.ContainsKey(document.Id))
                    throw new ContentException($"{resourceName}: duplicate reading id '{document.Id}'.");
                reading[document.Id] = document;
            }
```

And return it:

```csharp
        return new ContentLibrary(actions, setup, reading, terms);
```

- [ ] **Step 6: Embed the new folder**

In `src/GitHelper.Content/GitHelper.Content.csproj`, add to the existing `ItemGroup`:

```xml
    <EmbeddedResource Include="reading/**/*.md" />
```

Without this line the files are absent at runtime and the failure looks like missing content rather than missing configuration.

- [ ] **Step 7: Write the content**

Create `src/GitHelper.Content/terms/diff.md`:

```markdown
---
id: diff
title: diff
---
## definition
A list of only the lines that changed, rather than the whole file twice. Lines that were
added are marked with a plus, lines that were taken away with a minus, and a few unchanged
lines are shown around each change so you can see where in the file it happened.
```

Create `src/GitHelper.Content/terms/hunk.md`:

```markdown
---
id: hunk
title: hunk
---
## definition
One run of changes, with the unchanged lines around it. A file with two edits far apart is
shown as two hunks rather than one enormous stretch of unchanged text. The line beginning
with @@ says which lines of the old file and which lines of the new one this run covers.
```

Create `src/GitHelper.Content/reading/file-changes.md`:

```markdown
---
id: file-changes
title: What changed in this file
terms:
  - diff
  - hunk
  - staging-area
---

## what

This is a [[diff]]: only the lines that changed, not the whole file. A line starting with a
plus is one you added, a line starting with a minus is one you took away, and the plain lines
around them are there so you can see where the change sits.

Each block starting with @@ is a [[hunk]] — one run of changes and its surroundings. A file
edited in two distant places shows two of them.

There are two different things you can look at, and they answer different questions. Changes
you have not staged yet are the edits sitting in your files right now. Changes you have staged
are the ones already put in the [[staging-area]], waiting to go into your next commit. A file
can have both at once, if you staged it and then kept editing.
```

- [ ] **Step 8: Run the content tests**

Run: `dotnet test tests/GitHelper.Core.Tests --filter ContentIntegrityTests`
Expected: PASS. If `EveryGlossaryTermIsActuallyReferencedSomewhere` fails naming `diff` or `hunk`, the reading document is not being loaded — check the csproj line from Step 6.

- [ ] **Step 9: Run the whole suite**

Run: `dotnet test GitHelper.sln`
Expected: PASS, 653 tests.

- [ ] **Step 10: Commit**

```bash
git add src/GitHelper.Core/Content src/GitHelper.Content tests/GitHelper.Core.Tests/ContentIntegrityTests.cs
git commit -m "feat: add a content category for surfaces that are read, not run"
```

---

### Task 4: `DiffViewModel`

Owns one open diff and every sentence the surface can say. Tested against a fake reader, so no repository is involved.

**Files:**
- Create: `src/GitHelper.App/ViewModels/DiffViewModel.cs`
- Test: `tests/GitHelper.App.Tests/DiffViewModelTests.cs`

**Interfaces:**
- Consumes: `DiffReader`, `DiffSide`, `FileDiff`, `DiffKind` from Tasks 1-2; `GitReadException`; `ViewModelBase`.
- Produces: `IDiffSource` with `Task<FileDiff> ReadAsync(string repoPath, string path, DiffSide side, CancellationToken ct)`; `GitDiffSource(DiffReader)`; `DiffViewModel(IDiffSource)` with `OpenAsync(string repoPath, string path, DiffSide side, string? renamedFrom, CancellationToken ct)`, `RefreshAsync(CancellationToken ct)`, `CloseRequested` callback, and the observable properties `Path`, `SideLabel`, `Hunks`, `Message`, `HasMessage`, `HasHunks`.

- [ ] **Step 1: Write the failing tests**

Create `tests/GitHelper.App.Tests/DiffViewModelTests.cs`:

```csharp
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

        public Task<FileDiff> ReadAsync(string repoPath, string path, DiffSide side, CancellationToken ct)
        {
            Reads++;
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

    [Fact]
    public async Task PublishesHunksAndNamesTheSideOnOpen()
    {
        var source = new FakeSource { Next = () => TextDiff() };
        var viewModel = new DiffViewModel(source);

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
        var viewModel = new DiffViewModel(source);

        await viewModel.OpenAsync("repo", "a.txt", DiffSide.Staged, renamedFrom: null, default);

        Assert.Equal("Changes you have staged, ready to commit", viewModel.SideLabel);
    }

    [Fact]
    public async Task RefreshReReadsTheSameFile()
    {
        var source = new FakeSource { Next = () => TextDiff() };
        var viewModel = new DiffViewModel(source);
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
        var viewModel = new DiffViewModel(source);
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
        var viewModel = new DiffViewModel(source);

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
        var viewModel = new DiffViewModel(source);

        await viewModel.OpenAsync("repo", "new.txt", DiffSide.Staged, renamedFrom: "old.txt", default);

        Assert.Equal(
            "This file was renamed from old.txt. Its contents are unchanged.",
            viewModel.Message);
    }

    [Fact]
    public async Task ExplainsABinaryFile()
    {
        var source = new FakeSource { Next = () => Of(DiffKind.Binary) };
        var viewModel = new DiffViewModel(source);

        await viewModel.OpenAsync("repo", "logo.png", DiffSide.Unstaged, renamedFrom: null, default);

        Assert.Contains("not a text file", viewModel.Message);
    }

    [Fact]
    public async Task NamesTheRealCommandWhenTheDiffIsCutOff()
    {
        var source = new FakeSource { Next = () => TextDiff() with { Truncated = true } };
        var viewModel = new DiffViewModel(source);

        await viewModel.OpenAsync("repo", "a.txt", DiffSide.Unstaged, renamedFrom: null, default);

        Assert.True(viewModel.HasHunks);
        Assert.Contains("git diff -- a.txt", viewModel.Message);
    }

    [Fact]
    public async Task ClosingRaisesTheCallback()
    {
        var closed = false;
        var viewModel = new DiffViewModel(new FakeSource { Next = () => TextDiff() })
        {
            CloseRequested = () => closed = true,
        };

        viewModel.CloseCommand.Execute(null);

        Assert.True(closed);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GitHelper.App.Tests --filter DiffViewModelTests`
Expected: FAIL — `DiffViewModel` and `IDiffSource` do not exist.

- [ ] **Step 3: Write the viewmodel**

Create `src/GitHelper.App/ViewModels/DiffViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHelper.Core.Model;
using GitHelper.Core.Repo;

namespace GitHelper.App.ViewModels;

/// <summary>
/// What the viewmodel needs from the reader, and nothing more. An interface so the states
/// below can be tested without a repository on disk.
/// </summary>
public interface IDiffSource
{
    Task<FileDiff> ReadAsync(string repoPath, string path, DiffSide side, CancellationToken ct);
}

/// <summary>The real one.</summary>
public sealed class GitDiffSource(DiffReader reader) : IDiffSource
{
    public Task<FileDiff> ReadAsync(string repoPath, string path, DiffSide side, CancellationToken ct)
        => reader.ReadAsync(repoPath, path, side, ct);
}

/// <summary>
/// One file's diff, open. Transient: it is about a single file, and persisting it would mean
/// deciding what happens when that file stops existing.
/// </summary>
public sealed partial class DiffViewModel(IDiffSource source) : ViewModelBase
{
    private string? _repoPath;
    private DiffSide _side;
    private string? _renamedFrom;

    public ObservableCollection<DiffHunk> Hunks { get; } = new();

    [ObservableProperty] private string _path = string.Empty;
    [ObservableProperty] private string _sideLabel = string.Empty;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _hasMessage;
    [ObservableProperty] private bool _hasHunks;

    /// <summary>Raised when the user asks to go back. The shell decides what that means.</summary>
    public Action? CloseRequested { get; set; }

    private RelayCommand? _closeCommand;

    /// <summary>One instance, not a new command per binding: a fresh command each get would
    /// leave the view bound to an object nothing else can reach.</summary>
    public IRelayCommand CloseCommand =>
        _closeCommand ??= new RelayCommand(() => CloseRequested?.Invoke());

    public async Task OpenAsync(
        string repoPath,
        string path,
        DiffSide side,
        string? renamedFrom,
        CancellationToken ct)
    {
        _repoPath = repoPath;
        _side = side;
        _renamedFrom = renamedFrom;
        Path = path;
        SideLabel = side switch
        {
            DiffSide.Staged => "Changes you have staged, ready to commit",
            _ => "Changes you have not staged yet",
        };

        Hunks.Clear();
        Message = string.Empty;
        HasMessage = false;
        HasHunks = false;

        await ReadAsync(ct);
    }

    public Task RefreshAsync(CancellationToken ct)
        => _repoPath is null ? Task.CompletedTask : ReadAsync(ct);

    private async Task ReadAsync(CancellationToken ct)
    {
        FileDiff diff;
        try
        {
            diff = await source.ReadAsync(_repoPath!, Path, _side, ct);
        }
        catch (GitReadException)
        {
            // Keep whatever is on screen. Blanking it would say the user's changes had gone,
            // which is the most alarming possible lie and the reason RefreshAsync already
            // refuses to publish an empty snapshot on a failed read.
            Message = "The changes in this file could not be read just now.";
            HasMessage = true;
            return;
        }

        Hunks.Clear();
        foreach (var hunk in diff.Hunks) Hunks.Add(hunk);
        HasHunks = Hunks.Count > 0;

        Message = DescribeState(diff);
        HasMessage = Message.Length > 0;
    }

    private string DescribeState(FileDiff diff)
    {
        if (diff.Kind == DiffKind.Binary)
            return "This is not a text file, so there are no lines to compare. "
                + "Git can tell it changed, but not how.";

        if (diff.Truncated)
            return $"This file is too long to show in full. Showing the first "
                + $"{GitHelper.Core.Parsing.DiffParser.MaxLines} lines — to see all of it, run "
                + $"git diff -- {Path} in a terminal.";

        if (diff.Kind != DiffKind.Empty) return string.Empty;

        // Empty covers a rename with no edits, which is not "nothing changed" at all. The
        // diff genuinely does not carry the old name; the snapshot does.
        if (_renamedFrom is not null)
            return $"This file was renamed from {_renamedFrom}. Its contents are unchanged.";

        return _side == DiffSide.Staged
            ? "There are no staged changes in this file any more."
            : "There are no unstaged changes in this file any more.";
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/GitHelper.App.Tests --filter DiffViewModelTests`
Expected: PASS, 9 tests.

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test GitHelper.sln`
Expected: PASS, 662 tests.

- [ ] **Step 6: Commit**

```bash
git add src/GitHelper.App/ViewModels/DiffViewModel.cs tests/GitHelper.App.Tests/DiffViewModelTests.cs
git commit -m "feat: hold one open diff and every sentence it can say"
```

---

### Task 5: Opening and closing the surface

The row raises a request; the shell decides what it means. No child viewmodel learns about a sibling.

**Files:**
- Modify: `src/GitHelper.App/ViewModels/FileChangeRowViewModel.cs`
- Modify: `src/GitHelper.App/ViewModels/ChangesViewModel.cs`
- Modify: `src/GitHelper.App/ViewModels/MainViewModel.cs`
- Modify: `src/GitHelper.App/App.axaml.cs` (construct `DiffReader`, `GitDiffSource`, `DiffViewModel`)
- Test: `tests/GitHelper.App.Tests/MainViewModelTests.cs`

**Interfaces:**
- Consumes: `DiffViewModel`, `IDiffSource` from Task 4; `DiffSide` from Task 2.
- Produces: `FileChangeRowViewModel.ViewChangesCommand` and `CanViewChanges`; `ChangesViewModel.DiffRequestedAsync` callback of type `Func<string, DiffSide, string?, CancellationToken, Task>?`; `MainViewModel.OpenDiff` (nullable `DiffViewModel`).

- [ ] **Step 1: Write the failing tests**

Add to `tests/GitHelper.App.Tests/MainViewModelTests.cs` (follow the existing file's helper for building a `MainViewModel`; these assertions are the new part):

```csharp
    [Fact]
    public async Task ShowsTheDiffInPlaceOfTheCurrentTabWhenARowAsksForIt()
    {
        using var repo = await TestRepo.CreateAsync();
        var path = await repo.AddUnstagedChangeAsync();
        using var viewModel = NewFixture().Main;
        await viewModel.Startup.OpenAsync(repo.Path);

        await viewModel.Changes.Unstaged.Single(r => r.Path == path).ViewChangesCommand.ExecuteAsync(null);

        Assert.IsType<DiffViewModel>(viewModel.CurrentTab);
        Assert.Equal(path, ((DiffViewModel)viewModel.CurrentTab).Path);
    }

    [Fact]
    public async Task GoingBackRestoresTheTab()
    {
        using var repo = await TestRepo.CreateAsync();
        var path = await repo.AddUnstagedChangeAsync();
        using var viewModel = NewFixture().Main;
        await viewModel.Startup.OpenAsync(repo.Path);
        await viewModel.Changes.Unstaged.Single(r => r.Path == path).ViewChangesCommand.ExecuteAsync(null);

        ((DiffViewModel)viewModel.CurrentTab).CloseCommand.Execute(null);

        Assert.Same(viewModel.Changes, viewModel.CurrentTab);
    }

    /// <summary>
    /// The diff is about one file in one tab. Leaving that tab is leaving the question.
    /// </summary>
    [Fact]
    public async Task LeavingTheChangesTabClosesTheDiff()
    {
        using var repo = await TestRepo.CreateAsync();
        var path = await repo.AddUnstagedChangeAsync();
        using var viewModel = NewFixture().Main;
        await viewModel.Startup.OpenAsync(repo.Path);
        await viewModel.Changes.Unstaged.Single(r => r.Path == path).ViewChangesCommand.ExecuteAsync(null);

        viewModel.SelectedTab = MainTab.History;

        Assert.Same(viewModel.History, viewModel.CurrentTab);

        viewModel.SelectedTab = MainTab.Changes;

        Assert.Same(viewModel.Changes, viewModel.CurrentTab);
    }

    /// <summary>Conflicted files need combined diff format, which is v3's, not this surface's.</summary>
    [Fact]
    public async Task OffersNoDiffForAConflictedFile()
    {
        using var repo = await TestRepo.CreateAsync();
        await repo.StartConflictingMergeAsync();
        using var viewModel = NewFixture().Main;
        await viewModel.Startup.OpenAsync(repo.Path);

        Assert.NotEmpty(viewModel.Changes.Conflicted);
        Assert.All(viewModel.Changes.Conflicted, row => Assert.False(row.CanViewChanges));
    }
```

`NewFixture()` and `Startup.OpenAsync` are the existing helpers in that file — do not invent a second way to build the shell.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GitHelper.App.Tests --filter MainViewModelTests`
Expected: FAIL — `ViewChangesCommand`, `CanViewChanges`, and the diff swap do not exist.

- [ ] **Step 3: Give the row its button**

In `src/GitHelper.App/ViewModels/FileChangeRowViewModel.cs`, change the constructor signature and add the command. The row already knows `IsStaged` and `IsUntracked`, which is what decides the side:

```csharp
    public FileChangeRowViewModel(
        FileChange change,
        bool staged,
        Func<string, string, Task> invokeAction,
        Func<string, DiffSide, string?, Task>? viewChanges = null)
    {
        Path = change.Path;
        IsStaged = staged;
        IsUntracked = change.IsUntracked;
        IsUnmerged = change.IsUnmerged;
        StatusLabel = DescribeKind(staged ? change.IndexChange : change.WorkTreeChange);

        StageCommand = new AsyncRelayCommand(() => invokeAction("stage-file", change.Path));
        UnstageCommand = new AsyncRelayCommand(() => invokeAction("unstage-file", change.Path));
        DiscardCommand = new AsyncRelayCommand(() => invokeAction("discard-file", change.Path));
        MarkResolvedCommand = new AsyncRelayCommand(() => invokeAction("mark-resolved", change.Path));

        // A conflicted file's diff is git's combined format, a grammar this app does not read
        // yet. That surface belongs to guided conflict resolution, not here.
        CanViewChanges = viewChanges is not null && !change.IsUnmerged;

        var side = staged
            ? DiffSide.Staged
            : change.IsUntracked ? DiffSide.Untracked : DiffSide.Unstaged;

        ViewChangesCommand = new AsyncRelayCommand(
            () => viewChanges is null
                ? Task.CompletedTask
                : viewChanges(change.Path, side, change.OriginalPath),
            () => CanViewChanges);
    }
```

Add the members beside the other commands:

```csharp
    /// <summary>False for a conflicted file, which has no diff this app can read yet.</summary>
    public bool CanViewChanges { get; }

    public IAsyncRelayCommand ViewChangesCommand { get; }
```

Add `using GitHelper.Core.Repo;` at the top for `DiffSide`.

- [ ] **Step 4: Pass the request up from `ChangesViewModel`**

In `src/GitHelper.App/ViewModels/ChangesViewModel.cs`, add the callback beside the existing collections:

```csharp
    /// <summary>
    /// Raised when a row asks to see its changes. This viewmodel does not own the diff
    /// surface and does not know what showing it means — the shell decides, the same way it
    /// already does for opening a repository.
    /// </summary>
    public Func<string, DiffSide, string?, CancellationToken, Task>? DiffRequestedAsync { get; set; }
```

Add the private forwarder beside `InvokeWithPathAsync`:

```csharp
    private Task ViewChangesAsync(string path, DiffSide side, string? renamedFrom)
        => DiffRequestedAsync is null
            ? Task.CompletedTask
            : DiffRequestedAsync(path, side, renamedFrom, CancellationToken.None);
```

In `Update`, pass it to the two lists that get the button, and deliberately **not** to `Conflicted`:

```csharp
        Staged.Clear();
        foreach (var change in state.Staged)
            Staged.Add(new FileChangeRowViewModel(
                change, staged: true, InvokeWithPathAsync, ViewChangesAsync));

        Unstaged.Clear();
        // RepoState.Unstaged excludes untracked files by design; the view shows one
        // combined "not staged" list.
        foreach (var change in state.Unstaged.Concat(state.Untracked))
            Unstaged.Add(new FileChangeRowViewModel(
                change, staged: false, InvokeWithPathAsync, ViewChangesAsync));
```

Add `using GitHelper.Core.Repo;` at the top.

- [ ] **Step 5: Swap it into the middle column**

In `src/GitHelper.App/ViewModels/MainViewModel.cs`, take `DiffViewModel` as a constructor parameter, store it, and wire the two callbacks in the constructor body beside the existing ones:

```csharp
        Diff = diff;
        Diff.CloseRequested = () => OpenDiff = null;
        Changes.DiffRequestedAsync = async (path, side, renamedFrom, ct) =>
        {
            if (_repoPath is null) return;
            await Diff.OpenAsync(_repoPath, path, side, renamedFrom, ct);
            OpenDiff = Diff;
        };
```

Add the members:

```csharp
    public DiffViewModel Diff { get; }

    /// <summary>
    /// Non-null while a diff is open. It takes the middle column in place of the current tab
    /// rather than being a tab of its own: a diff is not a place you navigate to, it is a
    /// thing you open about one file.
    /// </summary>
    [ObservableProperty] private DiffViewModel? _openDiff;
```

Change `CurrentTab` so the open diff wins:

```csharp
    public ViewModelBase CurrentTab => OpenDiff ?? SelectedTab switch
    {
        MainTab.History => History,
        MainTab.Branches => Branches,
        _ => Changes,
    };
```

Republish it when the diff opens or closes, next to the existing `OnSelectedTabChanged`:

```csharp
    partial void OnOpenDiffChanged(DiffViewModel? value) => OnPropertyChanged(nameof(CurrentTab));
```

And close the diff when the user leaves the tab it belongs to:

```csharp
    partial void OnSelectedTabChanged(MainTab value)
    {
        // The diff is about one file in the Changes tab. Leaving that tab is leaving the
        // question, and a diff hanging over the History tab would be nonsense.
        OpenDiff = null;
        OnPropertyChanged(nameof(CurrentTab));
    }
```

Clear the callbacks in `Dispose`, beside the existing ones:

```csharp
        Changes.DiffRequestedAsync = null;
```

- [ ] **Step 6: Construct it, in the app and in the tests**

In `src/GitHelper.App/App.axaml.cs`, build the reader and viewmodel where the other viewmodels are built, and pass it to `MainViewModel`:

```csharp
        var diff = new DiffViewModel(new GitDiffSource(new DiffReader(runner)));
```

`runner` is the existing `SerializedGitRunner(new LoggingGitRunner(new GitRunner(), commandLog))` — reuse it rather than constructing a second one, or diff reads escape both the log and the per-repository gate.

`MainViewModel`'s constructor gains a parameter, so `NewFixture()` in `tests/GitHelper.App.Tests/MainViewModelTests.cs` must pass one too, built from the runner that fixture already makes:

```csharp
            new DiffViewModel(new GitDiffSource(new DiffReader(runner))),
```

Every other test file that constructs a `MainViewModel` needs the same argument. Find them with `grep -rn "new MainViewModel(" tests/` before running the suite.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/GitHelper.App.Tests --filter MainViewModelTests`
Expected: PASS.

- [ ] **Step 8: Run the whole suite**

Run: `dotnet test GitHelper.sln`
Expected: PASS, 666 tests.

- [ ] **Step 9: Commit**

```bash
git add src/GitHelper.App tests/GitHelper.App.Tests/MainViewModelTests.cs
git commit -m "feat: open a file's diff in place of the current tab"
```

---

### Task 6: The view

Rendering only. This codebase tests viewmodels rather than XAML, so the gate here is that the app builds, runs, and shows the thing.

**Files:**
- Create: `src/GitHelper.App/Views/DiffView.axaml`
- Create: `src/GitHelper.App/Views/DiffView.axaml.cs`
- Modify: `src/GitHelper.App/Views/MainWindow.axaml` (one more `DataTemplate`)
- Modify: `src/GitHelper.App/Views/ChangesView.axaml` (a button on the staged and unstaged row templates)

**Interfaces:**
- Consumes: `DiffViewModel` from Task 4, `FileChangeRowViewModel.ViewChangesCommand` and `CanViewChanges` from Task 5.
- Produces: nothing other tasks depend on.

- [ ] **Step 1: Add the view**

Create `src/GitHelper.App/Views/DiffView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:GitHelper.App.ViewModels"
             xmlns:model="using:GitHelper.Core.Model"
             x:DataType="vm:DiffViewModel"
             x:Class="GitHelper.App.Views.DiffView">

  <Grid RowDefinitions="Auto,Auto,*" Margin="12">

    <StackPanel Grid.Row="0" Spacing="2">
      <Grid ColumnDefinitions="*,Auto">
        <TextBlock Text="{Binding Path}" FontWeight="Bold" TextTrimming="CharacterEllipsis" />
        <Button Grid.Column="1" Content="Back to changes" Command="{Binding CloseCommand}" />
      </Grid>
      <TextBlock Text="{Binding SideLabel}" Opacity="0.6" FontSize="12" />
    </StackPanel>

    <TextBlock Grid.Row="1" Text="{Binding Message}" IsVisible="{Binding HasMessage}"
               TextWrapping="Wrap" Margin="0,8,0,0" />

    <!-- Horizontal scrolling, never wrapping: a wrapped line breaks the correspondence
         between screen rows and line numbers, and a diff whose numbers stop matching the
         file is worse than one you have to scroll. -->
    <ScrollViewer Grid.Row="2" Margin="0,8,0,0"
                  HorizontalScrollBarVisibility="Auto"
                  IsVisible="{Binding HasHunks}">
      <ItemsControl ItemsSource="{Binding Hunks}">
        <ItemsControl.ItemTemplate>
          <DataTemplate x:DataType="model:DiffHunk">
            <StackPanel Margin="0,0,0,10">

              <!-- The hunk header stays visible on purpose. It is jargon, and this app
                   underlines jargon rather than hiding it. -->
              <TextBlock Text="{Binding Header}"
                         FontFamily="Consolas, Cascadia Mono, Courier New, monospace"
                         Opacity="0.7" TextDecorations="Underline"
                         ToolTip.Tip="One run of changes and the unchanged lines around it. The numbers say which lines of the old file and which of the new one it covers." />

              <ItemsControl ItemsSource="{Binding Lines}">
                <ItemsControl.ItemTemplate>
                  <DataTemplate x:DataType="model:DiffLine">
                    <Grid ColumnDefinitions="40,40,*">
                      <TextBlock Text="{Binding OldLineNumber}" Opacity="0.4" FontSize="12"
                                 FontFamily="Consolas, Cascadia Mono, Courier New, monospace"
                                 TextAlignment="Right" Margin="0,0,6,0" />
                      <TextBlock Grid.Column="1" Text="{Binding NewLineNumber}" Opacity="0.4"
                                 FontSize="12"
                                 FontFamily="Consolas, Cascadia Mono, Courier New, monospace"
                                 TextAlignment="Right" Margin="0,0,6,0" />
                      <TextBlock Grid.Column="2" Text="{Binding Text}"
                                 FontFamily="Consolas, Cascadia Mono, Courier New, monospace" />
                    </Grid>
                  </DataTemplate>
                </ItemsControl.ItemTemplate>
              </ItemsControl>
            </StackPanel>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>
    </ScrollViewer>
  </Grid>
</UserControl>
```

Create `src/GitHelper.App/Views/DiffView.axaml.cs`, matching the other views in that folder:

```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GitHelper.App.Views;

public partial class DiffView : UserControl
{
    public DiffView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
```

Check an existing view's code-behind first and copy its exact shape — if the others use a generated `InitializeComponent`, do the same rather than hand-writing one.

- [ ] **Step 2: Register the template**

In `src/GitHelper.App/Views/MainWindow.axaml`, add inside `<Window.DataTemplates>`:

```xml
    <DataTemplate DataType="vm:DiffViewModel">
      <views:DiffView />
    </DataTemplate>
```

- [ ] **Step 3: Add the buttons**

In `src/GitHelper.App/Views/ChangesView.axaml`, in the **staged** row template, widen the grid and add the button before the Unstage button:

```xml
                <Grid ColumnDefinitions="*,Auto,Auto" Margin="0,3">
                  <StackPanel>
                    <TextBlock Text="{Binding Path}" TextTrimming="CharacterEllipsis" />
                    <TextBlock Text="{Binding StatusLabel}" Opacity="0.6" FontSize="12" />
                  </StackPanel>
                  <Button Grid.Column="1" Content="View changes"
                          Command="{Binding ViewChangesCommand}"
                          IsVisible="{Binding CanViewChanges}" />
                  <Button Grid.Column="2" Content="Unstage" Command="{Binding UnstageCommand}"
                          Margin="6,0,0,0" />
                </Grid>
```

And in the **unstaged** row template:

```xml
                <Grid ColumnDefinitions="*,Auto,Auto,Auto" Margin="0,3">
                  <StackPanel>
                    <TextBlock Text="{Binding Path}" TextTrimming="CharacterEllipsis" />
                    <TextBlock Text="{Binding StatusLabel}" Opacity="0.6" FontSize="12" />
                  </StackPanel>
                  <Button Grid.Column="1" Content="View changes"
                          Command="{Binding ViewChangesCommand}"
                          IsVisible="{Binding CanViewChanges}" />
                  <Button Grid.Column="2" Content="Stage" Command="{Binding StageCommand}"
                          Margin="6,0,0,0" />
                  <Button Grid.Column="3" Content="Discard" Command="{Binding DiscardCommand}"
                          Margin="6,0,0,0" />
                </Grid>
```

Leave the conflicted template alone.

- [ ] **Step 4: Build and run it**

Run: `dotnet build GitHelper.sln`
Expected: no warnings, no errors.

Then run the app against any repository with an edited file, and check by hand: the button appears on staged and unstaged rows and not on conflicted ones; clicking it shows the diff with line numbers; the header line is underlined and shows its tooltip on hover; a long line scrolls sideways rather than wrapping; Back to changes returns to the list.

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test GitHelper.sln`
Expected: PASS, 666 tests.

- [ ] **Step 6: Commit**

```bash
git add src/GitHelper.App/Views
git commit -m "feat: show a file's diff with its line numbers"
```

---

### Task 7: Keep the open diff fresh

The one rule this app never breaks: what is on screen is observed state, read fresh.

**Files:**
- Modify: `src/GitHelper.App/ViewModels/MainViewModel.cs` (`RefreshAsync`)
- Test: `tests/GitHelper.App.Tests/MainViewModelTests.cs`

**Interfaces:**
- Consumes: `DiffViewModel.RefreshAsync` from Task 4, `MainViewModel.OpenDiff` from Task 5.
- Produces: nothing new.

- [ ] **Step 1: Write the failing test**

Add to `tests/GitHelper.App.Tests/MainViewModelTests.cs`:

```csharp
    /// <summary>
    /// A diff read once and left alone would quietly disagree with the file list beside it.
    /// The file's status does not change when it is edited twice, so nothing but re-reading
    /// catches this.
    /// </summary>
    [Fact]
    public async Task RefreshingReReadsAnOpenDiff()
    {
        using var repo = await TestRepo.CreateAsync();
        var path = await repo.AddUnstagedChangeAsync();
        using var viewModel = NewFixture().Main;
        await viewModel.Startup.OpenAsync(repo.Path);
        await viewModel.Changes.Unstaged.Single(r => r.Path == path).ViewChangesCommand.ExecuteAsync(null);

        repo.WriteFile(path, "one\nEDITED AGAIN\nthree\n");
        await viewModel.RefreshAsync();

        var lines = viewModel.Diff.Hunks.SelectMany(h => h.Lines).ToList();
        Assert.Contains(lines, l => l.Text == "EDITED AGAIN");
        Assert.DoesNotContain(lines, l => l.Text == "TWO");
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/GitHelper.App.Tests --filter RefreshingReReadsAnOpenDiff`
Expected: FAIL — the diff still shows `TWO`.

- [ ] **Step 3: Re-read inside the refresh**

In `src/GitHelper.App/ViewModels/MainViewModel.cs`, in `RefreshAsync`, add after the existing `OperationBanner.Update(state);` line and still inside the `try`:

```csharp
            // Inside the refresh gate and on the same token: part of the refresh, not a race
            // beside it. Only while a diff is open, so a closed surface costs nothing.
            if (OpenDiff is not null) await OpenDiff.RefreshAsync(ct);
```

- [ ] **Step 4: Run it to verify it passes**

Run: `dotnet test tests/GitHelper.App.Tests --filter RefreshingReReadsAnOpenDiff`
Expected: PASS.

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test GitHelper.sln`
Expected: PASS, 667 tests.

- [ ] **Step 6: Commit**

```bash
git add src/GitHelper.App/ViewModels/MainViewModel.cs tests/GitHelper.App.Tests/MainViewModelTests.cs
git commit -m "fix: re-read an open diff on every refresh"
```

---

### Task 8: Documentation

**Files:**
- Modify: `docs/roadmap.md`
- Modify: `README.md`

- [ ] **Step 1: Update the roadmap**

In `docs/roadmap.md`, in the Sequence table, change the v2.5 row from `| **v2.5** | Diff viewer — **next** | ...` to a struck-through shipped row matching the rows above it:

```markdown
| **v2.5** | ~~Diff viewer~~ (shipped) | Independent of the above, and a prerequisite for v3 |
```

Mark v3 as next in the row below it. Then, in Bucket 3, replace the forward-looking paragraphs with what shipping it actually cost and what it left undone — that section currently predicts; it should now report, the way Bucket 2 does for merge and rebase. Name three things: that a read surface did not fit `ExplanationDocument` and content grew a fourth category rather than bending the existing one; that conflicted files were deliberately left out because git's combined diff format is a second grammar belonging to v3; and that `git diff --no-index` exits non-zero on success, which is the second time this codebase has been caught by an exit code that means something other than failure.

- [ ] **Step 2: Update the README**

In `README.md`, add the diff viewer to the description of what the Changes tab does. Keep it to a couple of sentences in the existing voice, and say the honest thing: it shows the real unified diff, with the jargon underlined rather than removed, because the app is meant to teach the format the user will meet everywhere else.

- [ ] **Step 3: Run the whole suite one more time**

Run: `dotnet test GitHelper.sln`
Expected: PASS, 667 tests.

- [ ] **Step 4: Commit**

```bash
git add docs/roadmap.md README.md
git commit -m "docs: mark the diff viewer shipped"
```

---

## Notes for the executor

**Test counts are predictions, not promises.** Each task states what the suite should total if the tests are written exactly as given. If your count differs because you split or merged a case, that is fine — what is not fine is a count going *down*, which means something got deleted.

**Two places where the natural implementation is wrong**, both already load-bearing in tests above:

1. `git diff --no-index` exits **1** when the files differ, which is every untracked file. Judging that read by `Success` reports every new file as broken.
2. A rename with no content change parses as `DiffKind.Empty`. Saying "no changes any more" about it is a flat lie, which is why `renamedFrom` is threaded from the snapshot into the viewmodel.

**No new `ErrorTranslator` rules.** Its rules are pattern-matched and scoped to action ids, and a diff read is not an action. A failed read gets the viewmodel sentence from Task 4. Inventing translations for failures nobody has seen would be guessing at a shape not yet visible.

**Do not add a diff button to conflicted rows** to be helpful. `git diff` gives combined diff format for unmerged paths, `DiffParser` does not read it, and the result would be an empty surface for the files a beginner most needs help with.

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-08-21-diff-viewer.md`. Two execution options:

**1. Subagent-Driven (recommended)** — a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — execute tasks in this session using executing-plans, batch execution with checkpoints.

Which approach?
