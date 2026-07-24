# GitHelper Core Engine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the headless engine for GitHelper — running git, parsing its output, describing every action as data, explaining it in plain English, and translating its errors — with no UI dependency.

**Architecture:** A single class library, `GitHelper.Core`, in which all git access funnels through one `GitRunner` that spawns the real `git` binary with argv arrays. Repository state is read into one immutable `RepoState` snapshot. Every write operation is a declarative `GitAction` descriptor carrying its own preconditions, danger level, and explanation id, so adding an action later is data rather than code. Authored Markdown content is parsed into a small closed block schema that the UI layer will render.

**Tech Stack:** C# on .NET 10 (`net10.0`), xUnit, YamlDotNet for content frontmatter. No Avalonia, no MVVM, no UI packages anywhere in this plan.

**This is Plan 1 of 2.** Plan 2 covers the Avalonia UI and is written after this plan is executed, so it can be written against interfaces that exist rather than guessed ones. The Avalonia version choice is deliberately deferred to Plan 2 — nothing here depends on it.

**Spec:** `docs/superpowers/specs/2026-07-24-git-helper-design.md`

## Global Constraints

- Target framework is `net10.0`. `Nullable` and `ImplicitUsings` enabled in every project.
- `GitHelper.Core` must never reference Avalonia or any UI package. This is load-bearing: it is what keeps the test suite headless and fast.
- Git is **never** invoked through a shell. Always `ProcessStartInfo.ArgumentList`, never `Arguments`.
- Every git invocation sets `StandardOutputEncoding`/`StandardErrorEncoding` to UTF-8 and passes `-c core.quotepath=false`.
- Every git invocation sets the environment variable `GIT_TERMINAL_PROMPT=0`.
- stdout and stderr are always read **concurrently**, before `WaitForExitAsync`. Reading them sequentially deadlocks once output exceeds a pipe buffer.
- The app never collects, stores, or transmits credentials. No code in this plan may accept a password or token.
- Every public async method accepts a `CancellationToken`.
- Term reference syntax in content is `[[term-id]]` or `[[term-id|display text]]`. Slot syntax is `{slotName}`.

## File Structure

| File | Responsibility |
|---|---|
| `src/GitHelper.Core/Git/GitCommandResult.cs` | Immutable result of one git invocation. |
| `src/GitHelper.Core/Git/IGitRunner.cs` | The one interface everything else depends on for git access. |
| `src/GitHelper.Core/Git/GitRunner.cs` | Process spawning, argv, UTF-8, concurrent stream reads. |
| `src/GitHelper.Core/Git/GitEnvironment.cs` | Startup checks: git present, version, identity configured. |
| `src/GitHelper.Core/Model/ChangeKind.cs` | Enum of file change kinds. |
| `src/GitHelper.Core/Model/FileChange.cs` | One changed path, with separate index and worktree status. |
| `src/GitHelper.Core/Model/CommitInfo.cs` | One commit. |
| `src/GitHelper.Core/Model/BranchInfo.cs` | One local branch and its upstream. |
| `src/GitHelper.Core/Model/RepoState.cs` | The immutable snapshot everything renders from. |
| `src/GitHelper.Core/Parsing/StatusParser.cs` | Parses `status --porcelain=v2 -z --branch`. |
| `src/GitHelper.Core/Parsing/LogParser.cs` | Parses the delimited `log` format. |
| `src/GitHelper.Core/Parsing/BranchParser.cs` | Parses the `for-each-ref` branch format. |
| `src/GitHelper.Core/Repo/RepoStateReader.cs` | Composes the three queries into one `RepoState`. |
| `src/GitHelper.Core/Content/ContentBlock.cs` | Closed block + inline span schema. |
| `src/GitHelper.Core/Content/ExplanationDocument.cs` | One parsed action content file. |
| `src/GitHelper.Core/Content/ContentParser.cs` | Frontmatter + Markdown-subset parser. |
| `src/GitHelper.Core/Content/ContentLibrary.cs` | Loads and indexes all embedded content. |
| `src/GitHelper.Core/Content/SlotBinder.cs` | Substitutes `{slot}` values from `RepoState`. |
| `src/GitHelper.Core/Actions/Danger.cs` | `Safe` / `Caution` / `Destructive`. |
| `src/GitHelper.Core/Actions/ActionRequest.cs` | Action id plus its parameters. |
| `src/GitHelper.Core/Actions/GitAction.cs` | The declarative descriptor. |
| `src/GitHelper.Core/Actions/Preconditions.cs` | The precondition interface and all implementations. |
| `src/GitHelper.Core/Actions/ActionCatalog.cs` | The 13 v1 action descriptors. |
| `src/GitHelper.Core/Actions/ActionService.cs` | Preview and run, with server-side re-validation. |
| `src/GitHelper.Core/Actions/Narrator.cs` | Describes the observed before/after difference. |
| `src/GitHelper.Core/Errors/ErrorTranslator.cs` | git stderr to plain English. |
| `src/GitHelper.Content/actions/*.md` | One authored explanation per action. |
| `src/GitHelper.Content/terms/*.md` | One glossary definition per term. |
| `tests/GitHelper.Core.Tests/TestRepo.cs` | Disposable temp repo helper for integration tests. |

---

### Task 1: Solution scaffold

**Files:**
- Create: `GitHelper.sln`
- Create: `src/GitHelper.Core/GitHelper.Core.csproj`
- Create: `tests/GitHelper.Core.Tests/GitHelper.Core.Tests.csproj`
- Create: `.gitignore`

**Interfaces:**
- Consumes: nothing.
- Produces: a solution where `dotnet test` runs green, with `GitHelper.Core` referenced by the test project.

- [ ] **Step 1: Create the solution and projects**

```bash
dotnet new gitignore
dotnet new sln -n GitHelper
dotnet new classlib -n GitHelper.Core -o src/GitHelper.Core -f net10.0
dotnet new xunit -n GitHelper.Core.Tests -o tests/GitHelper.Core.Tests -f net10.0
dotnet sln add src/GitHelper.Core/GitHelper.Core.csproj tests/GitHelper.Core.Tests/GitHelper.Core.Tests.csproj
dotnet add tests/GitHelper.Core.Tests/GitHelper.Core.Tests.csproj reference src/GitHelper.Core/GitHelper.Core.csproj
```

- [ ] **Step 2: Delete the template placeholder files**

```bash
rm -f src/GitHelper.Core/Class1.cs tests/GitHelper.Core.Tests/UnitTest1.cs
```

- [ ] **Step 3: Enable nullable and implicit usings in both projects**

Both `.csproj` files must contain this inside their existing `<PropertyGroup>`:

```xml
<TargetFramework>net10.0</TargetFramework>
<ImplicitUsings>enable</ImplicitUsings>
<Nullable>enable</Nullable>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
```

- [ ] **Step 4: Write a smoke test proving the harness runs**

Create `tests/GitHelper.Core.Tests/SmokeTest.cs`:

```csharp
namespace GitHelper.Core.Tests;

public class SmokeTest
{
    [Fact]
    public void TestHarnessRuns()
    {
        Assert.True(true);
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test`
Expected: PASS, 1 test.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "chore: scaffold GitHelper solution with Core and test projects"
```

---

### Task 2: GitRunner and the test repo helper

**Files:**
- Create: `src/GitHelper.Core/Git/GitCommandResult.cs`
- Create: `src/GitHelper.Core/Git/IGitRunner.cs`
- Create: `src/GitHelper.Core/Git/GitRunner.cs`
- Create: `tests/GitHelper.Core.Tests/TestRepo.cs`
- Test: `tests/GitHelper.Core.Tests/GitRunnerTests.cs`

**Interfaces:**
- Consumes: Task 1's projects.
- Produces:
  - `GitCommandResult(IReadOnlyList<string> ArgVector, string StdOut, string StdErr, int ExitCode, TimeSpan Duration)` with `bool Success` and `string CommandLine`.
  - `IGitRunner.RunAsync(string workingDirectory, IReadOnlyList<string> args, CancellationToken ct = default) -> Task<GitCommandResult>`
  - `TestRepo.CreateAsync(bool withInitialCommit = true) -> Task<TestRepo>`, with `string Path`, `WriteFile(string relativePath, string content)`, and `GitAsync(params string[] args)`.

Every later task depends on `IGitRunner` and `TestRepo`.

- [ ] **Step 1: Write the failing test**

Create `tests/GitHelper.Core.Tests/GitRunnerTests.cs`:

```csharp
using GitHelper.Core.Git;

namespace GitHelper.Core.Tests;

public class GitRunnerTests
{
    [Fact]
    public async Task RunAsync_ReportsSuccessAndCapturesStdOut()
    {
        using var repo = await TestRepo.CreateAsync();
        var runner = new GitRunner();

        var result = await runner.RunAsync(repo.Path, new[] { "rev-parse", "--abbrev-ref", "HEAD" });

        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("main", result.StdOut.Trim());
    }

    [Fact]
    public async Task RunAsync_ReportsFailureAndCapturesStdErr()
    {
        using var repo = await TestRepo.CreateAsync();
        var runner = new GitRunner();

        var result = await runner.RunAsync(repo.Path, new[] { "checkout", "no-such-branch" });

        Assert.False(result.Success);
        Assert.NotEqual(0, result.ExitCode);
        Assert.NotEmpty(result.StdErr);
    }

    [Fact]
    public async Task RunAsync_ArgVectorExcludesInternalFlagsSoTheTaughtCommandIsHonest()
    {
        using var repo = await TestRepo.CreateAsync();
        var runner = new GitRunner();

        var result = await runner.RunAsync(repo.Path, new[] { "status" });

        Assert.Equal("git status", result.CommandLine);
    }

    [Fact]
    public async Task RunAsync_HandlesPathsWithSpacesWithoutQuoting()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a file with spaces.txt", "hi\n");

        var runner = new GitRunner();
        var result = await runner.RunAsync(repo.Path, new[] { "add", "--", "a file with spaces.txt" });

        Assert.True(result.Success);
        var staged = await runner.RunAsync(repo.Path, new[] { "diff", "--cached", "--name-only" });
        Assert.Contains("a file with spaces.txt", staged.StdOut);
    }

    [Fact]
    public async Task RunAsync_ProducesLargeOutputWithoutDeadlocking()
    {
        using var repo = await TestRepo.CreateAsync();
        // Far larger than a pipe buffer; a sequential stream read would hang here.
        repo.WriteFile("big.txt", string.Join("\n", Enumerable.Range(0, 200_000).Select(i => $"line {i}")));

        var runner = new GitRunner();
        var task = runner.RunAsync(repo.Path, new[] { "status", "--porcelain", "--untracked-files=all" });
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(30)));

        Assert.Same(task, completed);
        Assert.True((await task).Success);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test`
Expected: FAIL — `GitRunner` and `TestRepo` do not exist (compile errors CS0246).

- [ ] **Step 3: Write GitCommandResult**

Create `src/GitHelper.Core/Git/GitCommandResult.cs`:

```csharp
namespace GitHelper.Core.Git;

/// <summary>The complete outcome of one git invocation.</summary>
/// <param name="ArgVector">
/// The user-facing arguments only. Internal flags such as -c core.quotepath=false are
/// deliberately excluded so that the command shown to the user is the command they could
/// type themselves.
/// </param>
public sealed record GitCommandResult(
    IReadOnlyList<string> ArgVector,
    string StdOut,
    string StdErr,
    int ExitCode,
    TimeSpan Duration)
{
    public bool Success => ExitCode == 0;

    /// <summary>The command as a user could type it. Used by the command log and explain panel.</summary>
    public string CommandLine => "git " + string.Join(' ', ArgVector);
}
```

- [ ] **Step 4: Write IGitRunner**

Create `src/GitHelper.Core/Git/IGitRunner.cs`:

```csharp
namespace GitHelper.Core.Git;

/// <summary>
/// The single choke point for git access. Nothing else in the application may
/// start a process.
/// </summary>
public interface IGitRunner
{
    Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> args,
        CancellationToken ct = default);
}
```

- [ ] **Step 5: Write GitRunner**

Create `src/GitHelper.Core/Git/GitRunner.cs`:

```csharp
using System.Diagnostics;
using System.Text;

namespace GitHelper.Core.Git;

public sealed class GitRunner : IGitRunner
{
    /// <summary>
    /// Prepended to every invocation but never shown to the user.
    /// core.quotepath=false stops git mangling non-ASCII filenames into octal escapes.
    /// </summary>
    private static readonly string[] InternalArgs = { "-c", "core.quotepath=false" };

    public async Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> args,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        // ArgumentList, never Arguments: the OS receives an argv array, so quoting
        // and injection defects cannot occur regardless of what a path contains.
        foreach (var a in InternalArgs) psi.ArgumentList.Add(a);
        foreach (var a in args) psi.ArgumentList.Add(a);

        // git must never block waiting on a prompt the user cannot see.
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";

        var stopwatch = Stopwatch.StartNew();
        using var process = new Process { StartInfo = psi };
        process.Start();

        // Both streams must be drained concurrently and before waiting for exit.
        // Reading one to completion first deadlocks as soon as the other fills its buffer.
        var stdOutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stdErrTask = process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        var stdOut = await stdOutTask.ConfigureAwait(false);
        var stdErr = await stdErrTask.ConfigureAwait(false);
        stopwatch.Stop();

        return new GitCommandResult(
            args.ToArray(),
            stdOut,
            stdErr,
            process.ExitCode,
            stopwatch.Elapsed);
    }
}
```

- [ ] **Step 6: Write the TestRepo helper**

Create `tests/GitHelper.Core.Tests/TestRepo.cs`:

```csharp
using GitHelper.Core.Git;

namespace GitHelper.Core.Tests;

/// <summary>A real git repository in a temp directory, deleted on dispose.</summary>
public sealed class TestRepo : IDisposable
{
    private static readonly GitRunner Runner = new();

    public string Path { get; }

    private TestRepo(string path) => Path = path;

    public static async Task<TestRepo> CreateAsync(bool withInitialCommit = true)
    {
        var dir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "githelper-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        var repo = new TestRepo(dir);
        await repo.GitAsync("init", "-q", "-b", "main");
        // Identity and signing are set locally so tests never depend on, or touch,
        // the developer's global git configuration.
        await repo.GitAsync("config", "user.name", "Test User");
        await repo.GitAsync("config", "user.email", "test@example.com");
        await repo.GitAsync("config", "commit.gpgsign", "false");

        if (withInitialCommit)
        {
            repo.WriteFile("README.md", "hello\n");
            await repo.GitAsync("add", "-A");
            await repo.GitAsync("commit", "-q", "-m", "initial");
        }

        return repo;
    }

    public void WriteFile(string relativePath, string content)
    {
        var full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    public Task<GitCommandResult> GitAsync(params string[] args)
        => Runner.RunAsync(Path, args);

    public void Dispose()
    {
        try
        {
            // Objects under .git are written read-only; plain recursive delete fails on Windows.
            foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A leaked temp directory must never fail a test run.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
```

- [ ] **Step 7: Run the tests**

Run: `dotnet test`
Expected: PASS, 6 tests.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: add GitRunner with argv-only invocation and concurrent stream reads"
```

---

### Task 3: State models and the status parser

**Files:**
- Create: `src/GitHelper.Core/Model/ChangeKind.cs`
- Create: `src/GitHelper.Core/Model/FileChange.cs`
- Create: `src/GitHelper.Core/Model/StatusSnapshot.cs`
- Create: `src/GitHelper.Core/Parsing/StatusParser.cs`
- Test: `tests/GitHelper.Core.Tests/StatusParserTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks — this is a pure parser over strings.
- Produces:
  - `enum ChangeKind { None, Added, Modified, Deleted, Renamed, Copied, Untracked, Unmerged }`
  - `FileChange(string Path, string? OriginalPath, ChangeKind IndexChange, ChangeKind WorkTreeChange)` with `bool IsStaged`, `bool HasUnstagedChanges`, `bool IsUntracked`.
  - `StatusSnapshot(string? Branch, bool IsDetached, bool HasCommits, string? Upstream, int Ahead, int Behind, IReadOnlyList<FileChange> Changes)`
  - `StatusParser.Parse(string porcelainV2ZOutput) -> StatusSnapshot`

**Background the implementer needs.** The command is `git status --porcelain=v2 -z --branch`. Its output is **NUL-separated, not line-separated**, so it must be split on `\0` and never on `\n`. Record types:

- `# branch.oid <hash>` — the literal `(initial)` means the repository has no commits yet.
- `# branch.head <name>` — the literal `(detached)` means detached HEAD.
- `# branch.upstream <name>` — absent entirely when there is no upstream.
- `# branch.ab +N -M` — ahead N, behind M. Absent when there is no upstream.
- `1 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <path>` — an ordinary change: 8 fields then the path.
- `2 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <Xscore> <path>` — a rename or copy: 9 fields then the path. **The original path is a separate NUL-terminated field immediately following this record**, so the parser must consume one extra element. This is the single easiest thing to get wrong.
- `u <XY> ... <path>` — unmerged: 10 fields then the path.
- `? <path>` — untracked.
- `! <path>` — ignored; skip it.

`XY` is two characters: index status then worktree status. `.` means unmodified.

Paths may contain spaces, so the path is taken as everything after the Nth space rather than by splitting the whole record.

- [ ] **Step 1: Write the failing test**

Create `tests/GitHelper.Core.Tests/StatusParserTests.cs`:

```csharp
using GitHelper.Core.Model;
using GitHelper.Core.Parsing;

namespace GitHelper.Core.Tests;

public class StatusParserTests
{
    /// <summary>Builds porcelain v2 -z output: every record is NUL-terminated.</summary>
    private static string Z(params string[] records)
        => string.Concat(records.Select(r => r + "\0"));

    [Fact]
    public void Parse_ReadsBranchUpstreamAndAheadBehind()
    {
        var input = Z(
            "# branch.oid a1b2c3d",
            "# branch.head main",
            "# branch.upstream origin/main",
            "# branch.ab +2 -3");

        var snapshot = StatusParser.Parse(input);

        Assert.Equal("main", snapshot.Branch);
        Assert.False(snapshot.IsDetached);
        Assert.True(snapshot.HasCommits);
        Assert.Equal("origin/main", snapshot.Upstream);
        Assert.Equal(2, snapshot.Ahead);
        Assert.Equal(3, snapshot.Behind);
    }

    [Fact]
    public void Parse_DetectsRepositoryWithNoCommits()
    {
        var input = Z("# branch.oid (initial)", "# branch.head main");

        var snapshot = StatusParser.Parse(input);

        Assert.False(snapshot.HasCommits);
        Assert.Equal("main", snapshot.Branch);
    }

    [Fact]
    public void Parse_DetectsDetachedHead()
    {
        var input = Z("# branch.oid a1b2c3d", "# branch.head (detached)");

        var snapshot = StatusParser.Parse(input);

        Assert.True(snapshot.IsDetached);
        Assert.Null(snapshot.Branch);
    }

    [Fact]
    public void Parse_ReadsStagedAndUnstagedStatusSeparately()
    {
        var input = Z(
            "# branch.oid a1b2c3d",
            "# branch.head main",
            "1 M. N... 100644 100644 100644 aaa bbb staged-only.txt",
            "1 .M N... 100644 100644 100644 aaa bbb worktree-only.txt",
            "1 MM N... 100644 100644 100644 aaa bbb both.txt");

        var snapshot = StatusParser.Parse(input);

        var stagedOnly = snapshot.Changes.Single(c => c.Path == "staged-only.txt");
        Assert.True(stagedOnly.IsStaged);
        Assert.False(stagedOnly.HasUnstagedChanges);

        var worktreeOnly = snapshot.Changes.Single(c => c.Path == "worktree-only.txt");
        Assert.False(worktreeOnly.IsStaged);
        Assert.True(worktreeOnly.HasUnstagedChanges);

        var both = snapshot.Changes.Single(c => c.Path == "both.txt");
        Assert.True(both.IsStaged);
        Assert.True(both.HasUnstagedChanges);
    }

    [Fact]
    public void Parse_ReadsRenameRecordAndItsTrailingOriginalPath()
    {
        // The original path is its own NUL-terminated field after the rename record.
        var input = Z(
            "# branch.oid a1b2c3d",
            "# branch.head main",
            "2 R. N... 100644 100644 100644 aaa bbb R100 new-name.txt",
            "old-name.txt",
            "? untracked-after-rename.txt");

        var snapshot = StatusParser.Parse(input);

        var renamed = snapshot.Changes.Single(c => c.Path == "new-name.txt");
        Assert.Equal(ChangeKind.Renamed, renamed.IndexChange);
        Assert.Equal("old-name.txt", renamed.OriginalPath);

        // Proves the extra field was consumed rather than parsed as its own record.
        Assert.Contains(snapshot.Changes, c => c.Path == "untracked-after-rename.txt");
        Assert.Equal(2, snapshot.Changes.Count);
    }

    [Fact]
    public void Parse_HandlesUntrackedUnmergedAndIgnoredRecords()
    {
        var input = Z(
            "# branch.oid a1b2c3d",
            "# branch.head main",
            "? new.txt",
            "u UU N... 100644 100644 100644 100644 aaa bbb ccc conflicted.txt",
            "! ignored.txt");

        var snapshot = StatusParser.Parse(input);

        Assert.Equal(ChangeKind.Untracked, snapshot.Changes.Single(c => c.Path == "new.txt").WorkTreeChange);
        Assert.Equal(ChangeKind.Unmerged, snapshot.Changes.Single(c => c.Path == "conflicted.txt").WorkTreeChange);
        Assert.DoesNotContain(snapshot.Changes, c => c.Path == "ignored.txt");
    }

    [Fact]
    public void Parse_PreservesPathsWithSpacesAndNonAsciiCharacters()
    {
        var input = Z(
            "# branch.oid a1b2c3d",
            "# branch.head main",
            "1 .M N... 100644 100644 100644 aaa bbb a file with spaces.txt",
            "? tara insir.txt");

        var snapshot = StatusParser.Parse(input);

        Assert.Contains(snapshot.Changes, c => c.Path == "a file with spaces.txt");
        Assert.Contains(snapshot.Changes, c => c.Path == "tara insir.txt");
    }

    [Fact]
    public void Parse_HandlesEmptyOutput()
    {
        var snapshot = StatusParser.Parse("");

        Assert.Empty(snapshot.Changes);
        Assert.False(snapshot.HasCommits);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter StatusParserTests`
Expected: FAIL — `StatusParser`, `StatusSnapshot`, `FileChange`, `ChangeKind` do not exist (CS0246).

- [ ] **Step 3: Write the models**

Create `src/GitHelper.Core/Model/ChangeKind.cs`:

```csharp
namespace GitHelper.Core.Model;

public enum ChangeKind
{
    None,
    Added,
    Modified,
    Deleted,
    Renamed,
    Copied,
    Untracked,
    Unmerged,
}
```

Create `src/GitHelper.Core/Model/FileChange.cs`:

```csharp
namespace GitHelper.Core.Model;

/// <summary>
/// One changed path. Index and worktree status are kept separate because a file can be
/// both staged and further modified afterwards, and the UI must show that honestly.
/// </summary>
public sealed record FileChange(
    string Path,
    string? OriginalPath,
    ChangeKind IndexChange,
    ChangeKind WorkTreeChange)
{
    public bool IsStaged => IndexChange != ChangeKind.None;

    public bool HasUnstagedChanges =>
        WorkTreeChange is not (ChangeKind.None or ChangeKind.Untracked);

    public bool IsUntracked => WorkTreeChange == ChangeKind.Untracked;
}
```

Create `src/GitHelper.Core/Model/StatusSnapshot.cs`:

```csharp
namespace GitHelper.Core.Model;

/// <summary>The parsed result of one status invocation.</summary>
public sealed record StatusSnapshot(
    string? Branch,
    bool IsDetached,
    bool HasCommits,
    string? Upstream,
    int Ahead,
    int Behind,
    IReadOnlyList<FileChange> Changes);
```

- [ ] **Step 4: Write the parser**

Create `src/GitHelper.Core/Parsing/StatusParser.cs`:

```csharp
using GitHelper.Core.Model;

namespace GitHelper.Core.Parsing;

/// <summary>Parses <c>git status --porcelain=v2 -z --branch</c>.</summary>
public static class StatusParser
{
    public static StatusSnapshot Parse(string output)
    {
        string? branch = null;
        var isDetached = false;
        var hasCommits = false;
        string? upstream = null;
        var ahead = 0;
        var behind = 0;
        var changes = new List<FileChange>();

        // Records are NUL-terminated, so the final split element is an empty tail.
        var records = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < records.Length; i++)
        {
            var record = records[i];
            if (record.Length == 0) continue;

            if (record[0] == '#')
            {
                ParseHeader(record, ref branch, ref isDetached, ref hasCommits,
                            ref upstream, ref ahead, ref behind);
                continue;
            }

            switch (record[0])
            {
                case '1':
                    changes.Add(ParseOrdinary(record));
                    break;

                case '2':
                    // The original path is the very next NUL-terminated field.
                    var originalPath = i + 1 < records.Length ? records[++i] : null;
                    changes.Add(ParseRenameOrCopy(record, originalPath));
                    break;

                case 'u':
                    changes.Add(new FileChange(
                        PathAfterFields(record, 10), null, ChangeKind.Unmerged, ChangeKind.Unmerged));
                    break;

                case '?':
                    changes.Add(new FileChange(
                        record[2..], null, ChangeKind.None, ChangeKind.Untracked));
                    break;

                case '!':
                    // Ignored files are not shown.
                    break;
            }
        }

        return new StatusSnapshot(branch, isDetached, hasCommits, upstream, ahead, behind, changes);
    }

    private static void ParseHeader(
        string record, ref string? branch, ref bool isDetached, ref bool hasCommits,
        ref string? upstream, ref int ahead, ref int behind)
    {
        var parts = record.Split(' ', 3);
        if (parts.Length < 3) return;

        switch (parts[1])
        {
            case "branch.oid":
                hasCommits = parts[2] != "(initial)";
                break;

            case "branch.head":
                if (parts[2] == "(detached)")
                {
                    isDetached = true;
                    branch = null;
                }
                else
                {
                    branch = parts[2];
                }
                break;

            case "branch.upstream":
                upstream = parts[2];
                break;

            case "branch.ab":
                // Format: "+2 -3"
                foreach (var token in parts[2].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (token.StartsWith('+') && int.TryParse(token[1..], out var a)) ahead = a;
                    else if (token.StartsWith('-') && int.TryParse(token[1..], out var b)) behind = b;
                }
                break;
        }
    }

    /// <summary>Ordinary record: 8 fixed fields, then the path.</summary>
    private static FileChange ParseOrdinary(string record)
    {
        var xy = record.Substring(2, 2);
        return new FileChange(
            PathAfterFields(record, 8),
            null,
            FromCode(xy[0]),
            FromCode(xy[1]));
    }

    /// <summary>Rename/copy record: 9 fixed fields (the extra one is the similarity score), then the path.</summary>
    private static FileChange ParseRenameOrCopy(string record, string? originalPath)
    {
        var xy = record.Substring(2, 2);
        return new FileChange(
            PathAfterFields(record, 9),
            originalPath,
            FromCode(xy[0]),
            FromCode(xy[1]));
    }

    /// <summary>
    /// Returns everything after the first <paramref name="fieldCount"/> space-separated fields.
    /// Splitting the whole record on space would corrupt any path containing a space.
    /// </summary>
    private static string PathAfterFields(string record, int fieldCount)
    {
        var index = 0;
        for (var field = 0; field < fieldCount; field++)
        {
            index = record.IndexOf(' ', index);
            if (index < 0) return string.Empty;
            index++;
        }
        return record[index..];
    }

    private static ChangeKind FromCode(char code) => code switch
    {
        '.' => ChangeKind.None,
        'A' => ChangeKind.Added,
        'M' => ChangeKind.Modified,
        'D' => ChangeKind.Deleted,
        'R' => ChangeKind.Renamed,
        'C' => ChangeKind.Copied,
        'U' => ChangeKind.Unmerged,
        'T' => ChangeKind.Modified, // type change; a beginner does not need the distinction
        _ => ChangeKind.None,
    };
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test --filter StatusParserTests`
Expected: PASS, 8 tests.

- [ ] **Step 6: Verify the parser against real git output**

Add to `tests/GitHelper.Core.Tests/StatusParserTests.cs`:

```csharp
    [Fact]
    public async Task Parse_MatchesRealGitOutput()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("staged.txt", "a\n");
        repo.WriteFile("untracked.txt", "b\n");
        await repo.GitAsync("add", "--", "staged.txt");

        var result = await repo.GitAsync("status", "--porcelain=v2", "-z", "--branch");
        var snapshot = StatusParser.Parse(result.StdOut);

        Assert.Equal("main", snapshot.Branch);
        Assert.True(snapshot.HasCommits);
        Assert.True(snapshot.Changes.Single(c => c.Path == "staged.txt").IsStaged);
        Assert.True(snapshot.Changes.Single(c => c.Path == "untracked.txt").IsUntracked);
    }
```

Run: `dotnet test --filter StatusParserTests`
Expected: PASS, 9 tests. This test is what proves the fixtures above are faithful rather than invented.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: parse git status porcelain v2 -z into a status snapshot"
```

---

### Task 4: Log and branch parsers

**Files:**
- Create: `src/GitHelper.Core/Model/CommitInfo.cs`
- Create: `src/GitHelper.Core/Model/BranchInfo.cs`
- Create: `src/GitHelper.Core/Parsing/LogParser.cs`
- Create: `src/GitHelper.Core/Parsing/BranchParser.cs`
- Test: `tests/GitHelper.Core.Tests/LogParserTests.cs`
- Test: `tests/GitHelper.Core.Tests/BranchParserTests.cs`

**Interfaces:**
- Consumes: `TestRepo` from Task 2.
- Produces:
  - `CommitInfo(string Hash, string ShortHash, string Author, DateTimeOffset Date, string Subject)`
  - `BranchInfo(string Name, string? Upstream)`
  - `LogParser.Format` — the constant `--format` string, and `LogParser.Parse(string output) -> IReadOnlyList<CommitInfo>`
  - `BranchParser.Format` — the constant `--format` string, and `BranchParser.Parse(string output) -> IReadOnlyList<BranchInfo>`

**Background the implementer needs.** Commit subjects can contain anything, including newlines and tabs, so neither can be used as a delimiter. The log format therefore uses ASCII control characters that cannot appear in commit metadata: **unit separator `%x1f`** between fields and **record separator `%x1e`** between commits.

Branch names are safer — git's own `check-ref-format` rejects control characters in refnames — so a tab (`%09`) is a sound field separator for `for-each-ref`.

`%(upstream:short)` expands to the empty string when a branch has no upstream, which must be read as `null` rather than `""`.

- [ ] **Step 1: Write the failing tests**

Create `tests/GitHelper.Core.Tests/LogParserTests.cs`:

```csharp
using GitHelper.Core.Parsing;

namespace GitHelper.Core.Tests;

public class LogParserTests
{
    private const string Unit = "\u001f";
    private const string Record = "\u001e";

    [Fact]
    public void Parse_ReadsAllCommitFields()
    {
        var input =
            $"a1b2c3d4e5f6{Unit}a1b2c3d{Unit}Ada Lovelace{Unit}2026-07-24T10:30:00+02:00{Unit}Add the thing{Record}";

        var commits = LogParser.Parse(input);

        var commit = Assert.Single(commits);
        Assert.Equal("a1b2c3d4e5f6", commit.Hash);
        Assert.Equal("a1b2c3d", commit.ShortHash);
        Assert.Equal("Ada Lovelace", commit.Author);
        Assert.Equal("Add the thing", commit.Subject);
        Assert.Equal(new DateTimeOffset(2026, 7, 24, 10, 30, 0, TimeSpan.FromHours(2)), commit.Date);
    }

    [Fact]
    public void Parse_ReadsMultipleCommitsInOrder()
    {
        var input =
            $"h2{Unit}h2{Unit}B{Unit}2026-07-24T10:00:00+00:00{Unit}second{Record}" +
            $"h1{Unit}h1{Unit}A{Unit}2026-07-23T10:00:00+00:00{Unit}first{Record}";

        var commits = LogParser.Parse(input);

        Assert.Equal(2, commits.Count);
        Assert.Equal("second", commits[0].Subject);
        Assert.Equal("first", commits[1].Subject);
    }

    [Fact]
    public void Parse_PreservesSubjectsContainingTabsAndNewlines()
    {
        var subject = "fix:\ttabbed\nand newlined";
        var input = $"h{Unit}h{Unit}A{Unit}2026-07-24T10:00:00+00:00{Unit}{subject}{Record}";

        var commits = LogParser.Parse(input);

        Assert.Equal(subject, Assert.Single(commits).Subject);
    }

    [Fact]
    public void Parse_HandlesEmptyOutputFromRepoWithNoCommits()
    {
        Assert.Empty(LogParser.Parse(""));
    }

    [Fact]
    public async Task Parse_MatchesRealGitOutput()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("second.txt", "x\n");
        await repo.GitAsync("add", "-A");
        await repo.GitAsync("commit", "-q", "-m", "second commit");

        var result = await repo.GitAsync("log", "--format=" + LogParser.Format, "-n", "50");
        var commits = LogParser.Parse(result.StdOut);

        Assert.Equal(2, commits.Count);
        Assert.Equal("second commit", commits[0].Subject);
        Assert.Equal("initial", commits[1].Subject);
        Assert.Equal("Test User", commits[0].Author);
    }
}
```

Create `tests/GitHelper.Core.Tests/BranchParserTests.cs`:

```csharp
using GitHelper.Core.Parsing;

namespace GitHelper.Core.Tests;

public class BranchParserTests
{
    [Fact]
    public void Parse_ReadsNameAndUpstream()
    {
        var input = "main\torigin/main\nfeature\t\n";

        var branches = BranchParser.Parse(input);

        Assert.Equal(2, branches.Count);
        Assert.Equal("main", branches[0].Name);
        Assert.Equal("origin/main", branches[0].Upstream);
        Assert.Equal("feature", branches[1].Name);
        Assert.Null(branches[1].Upstream);
    }

    [Fact]
    public void Parse_ReadsSlashedBranchNames()
    {
        var input = "feature/add-login\t\n";

        Assert.Equal("feature/add-login", Assert.Single(BranchParser.Parse(input)).Name);
    }

    [Fact]
    public void Parse_HandlesEmptyOutput()
    {
        Assert.Empty(BranchParser.Parse(""));
    }

    [Fact]
    public async Task Parse_MatchesRealGitOutput()
    {
        using var repo = await TestRepo.CreateAsync();
        await repo.GitAsync("branch", "feature");

        var result = await repo.GitAsync("for-each-ref", "--format=" + BranchParser.Format, "refs/heads/");
        var branches = BranchParser.Parse(result.StdOut);

        Assert.Equal(2, branches.Count);
        Assert.Contains(branches, b => b.Name == "main" && b.Upstream is null);
        Assert.Contains(branches, b => b.Name == "feature");
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter "LogParserTests|BranchParserTests"`
Expected: FAIL — `LogParser`, `BranchParser`, `CommitInfo`, `BranchInfo` do not exist (CS0246).

- [ ] **Step 3: Write the models**

Create `src/GitHelper.Core/Model/CommitInfo.cs`:

```csharp
namespace GitHelper.Core.Model;

public sealed record CommitInfo(
    string Hash,
    string ShortHash,
    string Author,
    DateTimeOffset Date,
    string Subject);
```

Create `src/GitHelper.Core/Model/BranchInfo.cs`:

```csharp
namespace GitHelper.Core.Model;

/// <summary>A local branch. <paramref name="Upstream"/> is null when none is configured.</summary>
public sealed record BranchInfo(string Name, string? Upstream);
```

- [ ] **Step 4: Write LogParser**

Create `src/GitHelper.Core/Parsing/LogParser.cs`:

```csharp
using System.Globalization;
using GitHelper.Core.Model;

namespace GitHelper.Core.Parsing;

/// <summary>Parses the delimited commit format produced by <see cref="Format"/>.</summary>
public static class LogParser
{
    private const char UnitSeparator = '\u001f';
    private const char RecordSeparator = '\u001e';

    /// <summary>
    /// Field and record separators are ASCII control characters, which cannot appear in
    /// commit metadata. A tab or newline delimiter would be corrupted by commit subjects.
    /// </summary>
    public const string Format = "%H%x1f%h%x1f%an%x1f%aI%x1f%s%x1e";

    public static IReadOnlyList<CommitInfo> Parse(string output)
    {
        var commits = new List<CommitInfo>();

        foreach (var record in output.Split(RecordSeparator))
        {
            // git separates records with newlines in addition to our separator.
            var trimmed = record.Trim('\n', '\r');
            if (trimmed.Length == 0) continue;

            var fields = trimmed.Split(UnitSeparator);
            if (fields.Length < 5) continue;

            var date = DateTimeOffset.TryParse(
                fields[3], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : DateTimeOffset.MinValue;

            commits.Add(new CommitInfo(fields[0], fields[1], fields[2], date, fields[4]));
        }

        return commits;
    }
}
```

- [ ] **Step 5: Write BranchParser**

Create `src/GitHelper.Core/Parsing/BranchParser.cs`:

```csharp
using GitHelper.Core.Model;

namespace GitHelper.Core.Parsing;

/// <summary>Parses the branch format produced by <see cref="Format"/>.</summary>
public static class BranchParser
{
    /// <summary>
    /// A tab is a safe separator here: git rejects control characters in refnames,
    /// so no branch name can contain one.
    /// </summary>
    public const string Format = "%(refname:short)%09%(upstream:short)";

    public static IReadOnlyList<BranchInfo> Parse(string output)
    {
        var branches = new List<BranchInfo>();

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.Length == 0) continue;

            var fields = trimmed.Split('\t');
            var name = fields[0];
            if (name.Length == 0) continue;

            // %(upstream:short) expands to empty when no upstream is configured.
            var upstream = fields.Length > 1 && fields[1].Length > 0 ? fields[1] : null;
            branches.Add(new BranchInfo(name, upstream));
        }

        return branches;
    }
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test --filter "LogParserTests|BranchParserTests"`
Expected: PASS, 9 tests.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: add log and branch parsers with control-character delimiters"
```

---

### Task 5: RepoStateReader

**Files:**
- Create: `src/GitHelper.Core/Model/RepoState.cs`
- Create: `src/GitHelper.Core/Repo/RepoStateReader.cs`
- Test: `tests/GitHelper.Core.Tests/RepoStateReaderTests.cs`

**Interfaces:**
- Consumes: `IGitRunner` (Task 2), `StatusParser`/`StatusSnapshot` (Task 3), `LogParser`/`BranchParser`/`CommitInfo`/`BranchInfo` (Task 4).
- Produces:
  - `RepoState(string RepoRoot, string? Branch, bool IsDetached, string? Upstream, int Ahead, int Behind, bool HasCommits, bool HasRemote, IReadOnlyList<FileChange> Changes, IReadOnlyList<CommitInfo> RecentCommits, IReadOnlyList<BranchInfo> Branches)` with computed `Staged`, `Unstaged`, `Untracked`, `HasStagedChanges`, `HasUncommittedChanges`, `CanUndoLastCommit`.
  - `RepoStateReader(IGitRunner runner)` with `ReadAsync(string repoPath, CancellationToken ct = default) -> Task<RepoState>`
  - `RepoStateReader.FindRepoRootAsync(string anyPathInsideRepo, CancellationToken ct = default) -> Task<string?>`

Every later task consumes `RepoState`.

- [ ] **Step 1: Write the failing test**

Create `tests/GitHelper.Core.Tests/RepoStateReaderTests.cs`:

```csharp
using GitHelper.Core.Git;
using GitHelper.Core.Repo;

namespace GitHelper.Core.Tests;

public class RepoStateReaderTests
{
    private static RepoStateReader NewReader() => new(new GitRunner());

    [Fact]
    public async Task ReadAsync_ReadsBranchCommitsAndChanges()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("staged.txt", "a\n");
        repo.WriteFile("untracked.txt", "b\n");
        await repo.GitAsync("add", "--", "staged.txt");

        var state = await NewReader().ReadAsync(repo.Path);

        Assert.Equal("main", state.Branch);
        Assert.False(state.IsDetached);
        Assert.True(state.HasCommits);
        Assert.False(state.HasRemote);
        Assert.Null(state.Upstream);
        Assert.True(state.HasStagedChanges);
        Assert.Single(state.Staged);
        Assert.Single(state.Untracked);
        Assert.Single(state.RecentCommits);
        Assert.Single(state.Branches);
    }

    [Fact]
    public async Task ReadAsync_HandlesRepositoryWithNoCommits()
    {
        using var repo = await TestRepo.CreateAsync(withInitialCommit: false);

        var state = await NewReader().ReadAsync(repo.Path);

        Assert.False(state.HasCommits);
        Assert.False(state.CanUndoLastCommit);
        Assert.Empty(state.RecentCommits);
        Assert.Empty(state.Branches); // no branch ref exists until the first commit
    }

    [Fact]
    public async Task ReadAsync_ReportsCanUndoLastCommitOnlyWhenAParentExists()
    {
        using var repo = await TestRepo.CreateAsync();

        var afterFirst = await NewReader().ReadAsync(repo.Path);
        Assert.False(afterFirst.CanUndoLastCommit);

        repo.WriteFile("second.txt", "x\n");
        await repo.GitAsync("add", "-A");
        await repo.GitAsync("commit", "-q", "-m", "second");

        var afterSecond = await NewReader().ReadAsync(repo.Path);
        Assert.True(afterSecond.CanUndoLastCommit);
    }

    [Fact]
    public async Task ReadAsync_DetectsDetachedHead()
    {
        using var repo = await TestRepo.CreateAsync();
        var head = (await repo.GitAsync("rev-parse", "HEAD")).StdOut.Trim();
        await repo.GitAsync("checkout", "-q", head);

        var state = await NewReader().ReadAsync(repo.Path);

        Assert.True(state.IsDetached);
        Assert.Null(state.Branch);
    }

    [Fact]
    public async Task FindRepoRootAsync_ReturnsNullOutsideARepository()
    {
        var dir = Path.Combine(Path.GetTempPath(), "githelper-notarepo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Null(await NewReader().FindRepoRootAsync(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task FindRepoRootAsync_FindsRootFromASubdirectory()
    {
        using var repo = await TestRepo.CreateAsync();
        var sub = Path.Combine(repo.Path, "nested", "deeper");
        Directory.CreateDirectory(sub);

        var root = await NewReader().FindRepoRootAsync(sub);

        Assert.NotNull(root);
        // Temp paths may differ by symlink or casing; compare resolved leaf identity.
        Assert.Equal(
            Path.GetFileName(repo.Path),
            Path.GetFileName(root!.TrimEnd('/', '\\')));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter RepoStateReaderTests`
Expected: FAIL — `RepoStateReader` and `RepoState` do not exist (CS0246).

- [ ] **Step 3: Write RepoState**

Create `src/GitHelper.Core/Model/RepoState.cs`:

```csharp
namespace GitHelper.Core.Model;

/// <summary>
/// One immutable snapshot of the repository. Every view renders from this, and every
/// precondition is evaluated against it.
/// </summary>
public sealed record RepoState(
    string RepoRoot,
    string? Branch,
    bool IsDetached,
    string? Upstream,
    int Ahead,
    int Behind,
    bool HasCommits,
    bool HasRemote,
    IReadOnlyList<FileChange> Changes,
    IReadOnlyList<CommitInfo> RecentCommits,
    IReadOnlyList<BranchInfo> Branches)
{
    public IReadOnlyList<FileChange> Staged =>
        Changes.Where(c => c.IsStaged).ToList();

    public IReadOnlyList<FileChange> Unstaged =>
        Changes.Where(c => c.HasUnstagedChanges).ToList();

    public IReadOnlyList<FileChange> Untracked =>
        Changes.Where(c => c.IsUntracked).ToList();

    public bool HasStagedChanges => Changes.Any(c => c.IsStaged);

    public bool HasUncommittedChanges =>
        Changes.Any(c => c.IsStaged || c.HasUnstagedChanges);

    /// <summary>
    /// False for the very first commit, which has no parent and therefore cannot be
    /// undone with reset --soft HEAD~1.
    /// </summary>
    public bool CanUndoLastCommit => RecentCommits.Count >= 2;
}
```

- [ ] **Step 4: Write RepoStateReader**

Create `src/GitHelper.Core/Repo/RepoStateReader.cs`:

```csharp
using GitHelper.Core.Git;
using GitHelper.Core.Model;
using GitHelper.Core.Parsing;

namespace GitHelper.Core.Repo;

/// <summary>Composes the individual read-only queries into one <see cref="RepoState"/>.</summary>
public sealed class RepoStateReader(IGitRunner runner)
{
    /// <summary>How many commits are loaded for the history view.</summary>
    public const int RecentCommitLimit = 50;

    public async Task<RepoState> ReadAsync(string repoPath, CancellationToken ct = default)
    {
        var statusResult = await runner.RunAsync(
            repoPath, new[] { "status", "--porcelain=v2", "-z", "--branch" }, ct);
        var status = StatusParser.Parse(statusResult.StdOut);

        var logResult = await runner.RunAsync(
            repoPath,
            new[] { "log", "--format=" + LogParser.Format, "-n", RecentCommitLimit.ToString() },
            ct);
        // A repository with no commits fails this command rather than returning nothing.
        var commits = logResult.Success
            ? LogParser.Parse(logResult.StdOut)
            : Array.Empty<CommitInfo>();

        var branchResult = await runner.RunAsync(
            repoPath,
            new[] { "for-each-ref", "--format=" + BranchParser.Format, "refs/heads/" },
            ct);
        var branches = BranchParser.Parse(branchResult.StdOut);

        var remoteResult = await runner.RunAsync(repoPath, new[] { "remote" }, ct);
        var hasRemote = remoteResult.Success && remoteResult.StdOut.Trim().Length > 0;

        return new RepoState(
            RepoRoot: repoPath,
            Branch: status.Branch,
            IsDetached: status.IsDetached,
            Upstream: status.Upstream,
            Ahead: status.Ahead,
            Behind: status.Behind,
            HasCommits: status.HasCommits,
            HasRemote: hasRemote,
            Changes: status.Changes,
            RecentCommits: commits,
            Branches: branches);
    }

    /// <summary>Returns the repository root containing the given path, or null if there is none.</summary>
    public async Task<string?> FindRepoRootAsync(string path, CancellationToken ct = default)
    {
        var result = await runner.RunAsync(path, new[] { "rev-parse", "--show-toplevel" }, ct);
        if (!result.Success) return null;

        var root = result.StdOut.Trim();
        return root.Length > 0 ? root : null;
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test --filter RepoStateReaderTests`
Expected: PASS, 6 tests.

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test`
Expected: PASS, all tests from Tasks 1–5.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: compose git queries into an immutable RepoState snapshot"
```

---

### Task 6: Content schema and parser

**Files:**
- Create: `src/GitHelper.Core/Actions/Danger.cs`
- Create: `src/GitHelper.Core/Content/ContentBlock.cs`
- Create: `src/GitHelper.Core/Content/ExplanationDocument.cs`
- Create: `src/GitHelper.Core/Content/ContentParser.cs`
- Modify: `src/GitHelper.Core/GitHelper.Core.csproj` (add YamlDotNet)
- Test: `tests/GitHelper.Core.Tests/ContentParserTests.cs`

**Interfaces:**
- Consumes: nothing — a pure parser over strings.
- Produces:
  - `enum Danger { Safe, Caution, Destructive }`
  - `abstract record ContentBlock` with `ParagraphBlock(IReadOnlyList<InlineSpan> Spans)`, `BulletListBlock(IReadOnlyList<IReadOnlyList<InlineSpan>> Items)`, `CodeBlock(string Text)`
  - `abstract record InlineSpan` with `TextSpan(string Text)`, `CodeSpan(string Text)`, `TermSpan(string TermId, string Display)`, `SlotSpan(string SlotName)`
  - `ExplanationDocument(string Id, string Title, Danger Danger, IReadOnlyList<string> Terms, string? UndoActionId, IReadOnlyList<ContentBlock> What, IReadOnlyList<ContentBlock> Risks, IReadOnlyList<ContentBlock> Undo)`
  - `ContentParser.Parse(string fileText, string sourceName) -> ExplanationDocument` (throws `ContentException` on malformed input)
  - `ContentException(string message)`

`Danger` lives in `Actions` rather than `Content` because the action catalog is its primary owner; the content frontmatter merely restates it, and Task 10 asserts the two agree.

**The content file format.** YAML frontmatter delimited by `---` lines, then exactly the sections `## what`, `## risks`, `## undo`. The parser accepts a deliberately small Markdown subset and **rejects anything outside it** rather than silently dropping it:

- A fenced block delimited by triple backticks becomes a `CodeBlock`.
- Consecutive lines beginning with `- ` become one `BulletListBlock`.
- Any other run of non-blank lines becomes one `ParagraphBlock`.

Inline, within paragraphs and bullets:

- `` `text` `` becomes a `CodeSpan`.
- `[[term-id]]` or `[[term-id|display text]]` becomes a `TermSpan`. With no display text the id is used verbatim.
- `{slotName}` becomes a `SlotSpan`.
- Everything else is a `TextSpan`.

- [ ] **Step 1: Add the YamlDotNet package**

```bash
dotnet add src/GitHelper.Core/GitHelper.Core.csproj package YamlDotNet
```

- [ ] **Step 2: Write the failing test**

Create `tests/GitHelper.Core.Tests/ContentParserTests.cs`:

```csharp
using GitHelper.Core.Actions;
using GitHelper.Core.Content;

namespace GitHelper.Core.Tests;

public class ContentParserTests
{
    private const string Minimal = """
        ---
        id: commit
        title: Commit
        danger: caution
        terms: [staging-area, commit]
        undo: undo-last-commit
        ---
        ## what
        Saves a snapshot.

        ## risks
        Nothing serious.

        ## undo
        Use undo last commit.
        """;

    [Fact]
    public void Parse_ReadsFrontmatter()
    {
        var doc = ContentParser.Parse(Minimal, "commit.md");

        Assert.Equal("commit", doc.Id);
        Assert.Equal("Commit", doc.Title);
        Assert.Equal(Danger.Caution, doc.Danger);
        Assert.Equal(new[] { "staging-area", "commit" }, doc.Terms);
        Assert.Equal("undo-last-commit", doc.UndoActionId);
    }

    [Fact]
    public void Parse_SplitsTheThreeSections()
    {
        var doc = ContentParser.Parse(Minimal, "commit.md");

        Assert.Single(doc.What);
        Assert.Single(doc.Risks);
        Assert.Single(doc.Undo);
    }

    [Fact]
    public void Parse_ReadsParagraphsBulletsAndCodeBlocks()
    {
        var text = """
            ---
            id: x
            title: X
            danger: safe
            ---
            ## what
            A paragraph.

            - first bullet
            - second bullet

            ```
            git status
            ```

            ## risks
            None.

            ## undo
            Nothing to undo.
            """;

        var doc = ContentParser.Parse(text, "x.md");

        Assert.Equal(3, doc.What.Count);
        Assert.IsType<ParagraphBlock>(doc.What[0]);

        var bullets = Assert.IsType<BulletListBlock>(doc.What[1]);
        Assert.Equal(2, bullets.Items.Count);

        var code = Assert.IsType<CodeBlock>(doc.What[2]);
        Assert.Equal("git status", code.Text);
    }

    [Fact]
    public void Parse_ReadsInlineCodeTermsAndSlots()
    {
        var text = """
            ---
            id: x
            title: X
            danger: safe
            ---
            ## what
            Run `git add` on {stagedCount} files in the [[staging-area|staging area]] on [[HEAD]].

            ## risks
            None.

            ## undo
            None.
            """;

        var doc = ContentParser.Parse(text, "x.md");
        var spans = Assert.IsType<ParagraphBlock>(doc.What[0]).Spans;

        Assert.Contains(spans, s => s is CodeSpan { Text: "git add" });
        Assert.Contains(spans, s => s is SlotSpan { SlotName: "stagedCount" });
        Assert.Contains(spans, s => s is TermSpan { TermId: "staging-area", Display: "staging area" });
        // With no display text the id is shown verbatim.
        Assert.Contains(spans, s => s is TermSpan { TermId: "HEAD", Display: "HEAD" });
    }

    [Fact]
    public void Parse_TreatsUndoAsOptionalOnlyInFrontmatter()
    {
        var text = """
            ---
            id: x
            title: X
            danger: safe
            ---
            ## what
            Something.

            ## risks
            None.

            ## undo
            None needed.
            """;

        var doc = ContentParser.Parse(text, "x.md");

        Assert.Null(doc.UndoActionId);
        Assert.NotEmpty(doc.Undo);
    }

    [Theory]
    [InlineData("no frontmatter at all", "frontmatter")]
    [InlineData("---\nid: x\ntitle: X\ndanger: safe\n---\n## what\nOnly one section.", "risks")]
    public void Parse_RejectsMalformedContent(string text, string expectedMessageFragment)
    {
        var ex = Assert.Throws<ContentException>(() => ContentParser.Parse(text, "bad.md"));

        Assert.Contains(expectedMessageFragment, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bad.md", ex.Message);
    }

    [Fact]
    public void Parse_RejectsUnknownSectionRatherThanDroppingIt()
    {
        var text = """
            ---
            id: x
            title: X
            danger: safe
            ---
            ## what
            A.

            ## risks
            B.

            ## undo
            C.

            ## surprise
            D.
            """;

        var ex = Assert.Throws<ContentException>(() => ContentParser.Parse(text, "x.md"));

        Assert.Contains("surprise", ex.Message);
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test --filter ContentParserTests`
Expected: FAIL — `ContentParser`, `ExplanationDocument`, `ContentBlock`, `Danger` do not exist (CS0246).

- [ ] **Step 4: Write Danger**

Create `src/GitHelper.Core/Actions/Danger.cs`:

```csharp
namespace GitHelper.Core.Actions;

/// <summary>How much a user should be slowed down before an action runs.</summary>
public enum Danger
{
    /// <summary>Runs immediately; the explanation is shown alongside.</summary>
    Safe,

    /// <summary>Requires an explicit confirmation.</summary>
    Caution,

    /// <summary>Requires confirmation plus a consequence sentence. Never suppressible.</summary>
    Destructive,
}
```

- [ ] **Step 5: Write the block schema**

Create `src/GitHelper.Core/Content/ContentBlock.cs`:

```csharp
namespace GitHelper.Core.Content;

/// <summary>
/// A closed schema. The UI renders exactly these cases, so any content the parser
/// cannot express is a content error rather than something silently dropped.
/// </summary>
public abstract record ContentBlock;

public sealed record ParagraphBlock(IReadOnlyList<InlineSpan> Spans) : ContentBlock;

public sealed record BulletListBlock(IReadOnlyList<IReadOnlyList<InlineSpan>> Items) : ContentBlock;

public sealed record CodeBlock(string Text) : ContentBlock;

public abstract record InlineSpan;

public sealed record TextSpan(string Text) : InlineSpan;

public sealed record CodeSpan(string Text) : InlineSpan;

/// <summary>A glossary reference. The UI underlines it and shows the definition on hover.</summary>
public sealed record TermSpan(string TermId, string Display) : InlineSpan;

/// <summary>A placeholder filled from RepoState at render time.</summary>
public sealed record SlotSpan(string SlotName) : InlineSpan;
```

- [ ] **Step 6: Write ExplanationDocument and ContentException**

Create `src/GitHelper.Core/Content/ExplanationDocument.cs`:

```csharp
using GitHelper.Core.Actions;

namespace GitHelper.Core.Content;

/// <summary>One authored action explanation, parsed.</summary>
public sealed record ExplanationDocument(
    string Id,
    string Title,
    Danger Danger,
    IReadOnlyList<string> Terms,
    string? UndoActionId,
    IReadOnlyList<ContentBlock> What,
    IReadOnlyList<ContentBlock> Risks,
    IReadOnlyList<ContentBlock> Undo);

/// <summary>Thrown when a content file does not match the schema. Always names its source file.</summary>
public sealed class ContentException(string message) : Exception(message);
```

- [ ] **Step 7: Write ContentParser**

Create `src/GitHelper.Core/Content/ContentParser.cs`:

```csharp
using System.Text;
using System.Text.RegularExpressions;
using GitHelper.Core.Actions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace GitHelper.Core.Content;

/// <summary>Parses an authored content file into <see cref="ExplanationDocument"/>.</summary>
public static partial class ContentParser
{
    private static readonly string[] RequiredSections = { "what", "risks", "undo" };

    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private sealed class Frontmatter
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Danger { get; set; }
        public List<string>? Terms { get; set; }
        public string? Undo { get; set; }
    }

    public static ExplanationDocument Parse(string fileText, string sourceName)
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
        if (!Enum.TryParse<Danger>(matter.Danger, ignoreCase: true, out var danger))
            throw new ContentException(
                $"{sourceName}: frontmatter 'danger' must be safe, caution, or destructive.");

        var sections = SplitSections(body, sourceName);

        foreach (var required in RequiredSections)
        {
            if (!sections.ContainsKey(required))
                throw new ContentException($"{sourceName}: missing required section '## {required}'.");
        }

        return new ExplanationDocument(
            Id: matter.Id!,
            Title: matter.Title!,
            Danger: danger,
            Terms: matter.Terms ?? new List<string>(),
            UndoActionId: string.IsNullOrWhiteSpace(matter.Undo) ? null : matter.Undo,
            What: ParseBlocks(sections["what"]),
            Risks: ParseBlocks(sections["risks"]),
            Undo: ParseBlocks(sections["undo"]));
    }

    private static (string Frontmatter, string Body) SplitFrontmatter(string fileText, string sourceName)
    {
        var normalized = fileText.Replace("\r\n", "\n");
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
            throw new ContentException($"{sourceName}: file must begin with '---' frontmatter.");

        var end = normalized.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0)
            throw new ContentException($"{sourceName}: frontmatter is not closed with '---'.");

        var frontmatter = normalized[4..end];
        var afterMarker = normalized.IndexOf('\n', end + 1);
        var body = afterMarker < 0 ? string.Empty : normalized[(afterMarker + 1)..];
        return (frontmatter, body);
    }

    private static Dictionary<string, string> SplitSections(string body, string sourceName)
    {
        var sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? current = null;
        var buffer = new StringBuilder();

        void Flush()
        {
            if (current is not null) sections[current] = buffer.ToString();
            buffer.Clear();
        }

        foreach (var line in body.Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                Flush();
                current = line[3..].Trim();
                if (!RequiredSections.Contains(current, StringComparer.OrdinalIgnoreCase))
                    throw new ContentException(
                        $"{sourceName}: unknown section '## {current}'. Allowed: what, risks, undo.");
                continue;
            }

            if (current is not null) buffer.Append(line).Append('\n');
        }

        Flush();
        return sections;
    }

    private static IReadOnlyList<ContentBlock> ParseBlocks(string section)
    {
        var blocks = new List<ContentBlock>();
        var lines = section.Split('\n');
        var i = 0;

        while (i < lines.Length)
        {
            var line = lines[i];

            if (line.Trim().Length == 0)
            {
                i++;
                continue;
            }

            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                i++;
                var code = new List<string>();
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
                    code.Add(lines[i++]);
                i++; // closing fence
                blocks.Add(new CodeBlock(string.Join('\n', code).Trim('\n')));
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                var items = new List<IReadOnlyList<InlineSpan>>();
                while (i < lines.Length && lines[i].StartsWith("- ", StringComparison.Ordinal))
                    items.Add(ParseInline(lines[i++][2..]));
                blocks.Add(new BulletListBlock(items));
                continue;
            }

            var paragraph = new List<string>();
            while (i < lines.Length
                   && lines[i].Trim().Length > 0
                   && !lines[i].StartsWith("- ", StringComparison.Ordinal)
                   && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                paragraph.Add(lines[i++].Trim());
            }

            blocks.Add(new ParagraphBlock(ParseInline(string.Join(' ', paragraph))));
        }

        return blocks;
    }

    // Matches, in one pass: `code`, [[term]] or [[term|display]], and {slot}.
    [GeneratedRegex(@"`(?<code>[^`]+)`|\[\[(?<term>[^\]|]+)(\|(?<display>[^\]]+))?\]\]|\{(?<slot>[A-Za-z][A-Za-z0-9]*)\}")]
    private static partial Regex InlinePattern();

    private static IReadOnlyList<InlineSpan> ParseInline(string text)
    {
        var spans = new List<InlineSpan>();
        var position = 0;

        foreach (Match match in InlinePattern().Matches(text))
        {
            if (match.Index > position)
                spans.Add(new TextSpan(text[position..match.Index]));

            if (match.Groups["code"].Success)
            {
                spans.Add(new CodeSpan(match.Groups["code"].Value));
            }
            else if (match.Groups["term"].Success)
            {
                var id = match.Groups["term"].Value;
                var display = match.Groups["display"].Success ? match.Groups["display"].Value : id;
                spans.Add(new TermSpan(id, display));
            }
            else
            {
                spans.Add(new SlotSpan(match.Groups["slot"].Value));
            }

            position = match.Index + match.Length;
        }

        if (position < text.Length)
            spans.Add(new TextSpan(text[position..]));

        return spans;
    }
}
```

- [ ] **Step 8: Run the tests**

Run: `dotnet test --filter ContentParserTests`
Expected: PASS, 8 tests.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: parse authored content into a closed block schema"
```

---

### Task 7: Content project, library, and slot binding

**Files:**
- Create: `src/GitHelper.Content/GitHelper.Content.csproj`
- Create: `src/GitHelper.Content/ContentAssembly.cs`
- Create: `src/GitHelper.Content/terms/staging-area.md`
- Create: `src/GitHelper.Content/actions/stage-file.md`
- Create: `src/GitHelper.Core/Content/GlossaryTerm.cs`
- Create: `src/GitHelper.Core/Content/ContentLibrary.cs`
- Create: `src/GitHelper.Core/Content/SlotBinder.cs`
- Modify: `src/GitHelper.Core/GitHelper.Core.csproj` (reference GitHelper.Content)
- Modify: `GitHelper.sln`
- Test: `tests/GitHelper.Core.Tests/ContentLibraryTests.cs`
- Test: `tests/GitHelper.Core.Tests/SlotBinderTests.cs`

**Interfaces:**
- Consumes: `ContentParser`, `ExplanationDocument`, `ContentException` (Task 6); `RepoState` (Task 5).
- Produces:
  - `GlossaryTerm(string Id, string Title, IReadOnlyList<ContentBlock> Definition)`
  - `ContentLibrary.Load() -> ContentLibrary` (loads every embedded file; throws `ContentException` naming the offending file)
  - `ContentLibrary.Actions -> IReadOnlyDictionary<string, ExplanationDocument>`
  - `ContentLibrary.Terms -> IReadOnlyDictionary<string, GlossaryTerm>`
  - `SlotBinder.KnownSlots -> IReadOnlySet<string>`
  - `SlotBinder.Bind(RepoState state, string? path = null, string? branchName = null) -> IReadOnlyDictionary<string, string>`

Only two content files are authored in this task — just enough to prove loading works. The remaining eleven actions and all other terms are authored in Task 10, once the action catalog fixes their ids.

**Glossary files** use the same frontmatter parser but a different, simpler shape: `id`, `title`, and a single `## definition` section. They are parsed by `ContentLibrary` directly rather than by `ContentParser`, whose required sections are action-specific.

- [ ] **Step 1: Create the content project**

```bash
dotnet new classlib -n GitHelper.Content -o src/GitHelper.Content -f net10.0
rm -f src/GitHelper.Content/Class1.cs
dotnet sln add src/GitHelper.Content/GitHelper.Content.csproj
dotnet add src/GitHelper.Core/GitHelper.Core.csproj reference src/GitHelper.Content/GitHelper.Content.csproj
```

- [ ] **Step 2: Embed the content files**

Replace the contents of `src/GitHelper.Content/GitHelper.Content.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>

  <ItemGroup>
    <EmbeddedResource Include="actions/**/*.md" />
    <EmbeddedResource Include="terms/**/*.md" />
  </ItemGroup>

</Project>
```

Create `src/GitHelper.Content/ContentAssembly.cs`:

```csharp
using System.Reflection;

namespace GitHelper.Content;

/// <summary>Marker giving the core library a handle on the assembly holding the content files.</summary>
public static class ContentAssembly
{
    public static Assembly Value => typeof(ContentAssembly).Assembly;
}
```

- [ ] **Step 3: Author the two starter content files**

Create `src/GitHelper.Content/terms/staging-area.md`:

```markdown
---
id: staging-area
title: staging area
---
## definition
A holding area for the changes you want in your next save. Editing a file does not
put it here; you choose what goes in. This lets you save some of your work without
saving all of it.
```

Create `src/GitHelper.Content/actions/stage-file.md`:

```markdown
---
id: stage-file
title: Stage file
danger: safe
terms: [staging-area]
---
## what
Adds this file to the [[staging-area]], so it will be included the next time you save
your work. Nothing is saved permanently yet, and the file itself is not changed.

## risks
Nothing. Staging is easy to reverse, and it never alters the contents of your files.

## undo
Unstage the file. It goes back to being a change you have not chosen yet.
```

- [ ] **Step 4: Write the failing tests**

Create `tests/GitHelper.Core.Tests/ContentLibraryTests.cs`:

```csharp
using GitHelper.Core.Actions;
using GitHelper.Core.Content;

namespace GitHelper.Core.Tests;

public class ContentLibraryTests
{
    [Fact]
    public void Load_ReadsEmbeddedActionFiles()
    {
        var library = ContentLibrary.Load();

        var stageFile = library.Actions["stage-file"];
        Assert.Equal("Stage file", stageFile.Title);
        Assert.Equal(Danger.Safe, stageFile.Danger);
        Assert.Contains("staging-area", stageFile.Terms);
        Assert.NotEmpty(stageFile.What);
    }

    [Fact]
    public void Load_ReadsEmbeddedGlossaryFiles()
    {
        var library = ContentLibrary.Load();

        var term = library.Terms["staging-area"];
        Assert.Equal("staging area", term.Title);
        Assert.NotEmpty(term.Definition);
    }

    [Fact]
    public void Load_IsCaseInsensitiveOnIds()
    {
        var library = ContentLibrary.Load();

        Assert.True(library.Actions.ContainsKey("STAGE-FILE"));
    }
}
```

Create `tests/GitHelper.Core.Tests/SlotBinderTests.cs`:

```csharp
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
            Branches: Array.Empty<BranchInfo>());

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
}
```

- [ ] **Step 5: Run to verify they fail**

Run: `dotnet test --filter "ContentLibraryTests|SlotBinderTests"`
Expected: FAIL — `ContentLibrary`, `GlossaryTerm`, `SlotBinder` do not exist (CS0246).

- [ ] **Step 6: Write GlossaryTerm**

Create `src/GitHelper.Core/Content/GlossaryTerm.cs`:

```csharp
namespace GitHelper.Core.Content;

/// <summary>
/// One glossary definition. Defined exactly once and referenced by id everywhere, so
/// correcting a poor explanation corrects it in every place it appears.
/// </summary>
public sealed record GlossaryTerm(
    string Id,
    string Title,
    IReadOnlyList<ContentBlock> Definition);
```

- [ ] **Step 7: Write ContentLibrary**

Create `src/GitHelper.Core/Content/ContentLibrary.cs`:

```csharp
using System.Reflection;

namespace GitHelper.Core.Content;

/// <summary>Loads and indexes every embedded content file.</summary>
public sealed class ContentLibrary
{
    public IReadOnlyDictionary<string, ExplanationDocument> Actions { get; }
    public IReadOnlyDictionary<string, GlossaryTerm> Terms { get; }

    private ContentLibrary(
        IReadOnlyDictionary<string, ExplanationDocument> actions,
        IReadOnlyDictionary<string, GlossaryTerm> terms)
    {
        Actions = actions;
        Terms = terms;
    }

    // Fully qualified: this type sits in GitHelper.Core.Content, so an unqualified
    // "Content.ContentAssembly" would bind to the wrong namespace.
    public static ContentLibrary Load() => Load(global::GitHelper.Content.ContentAssembly.Value);

    public static ContentLibrary Load(Assembly assembly)
    {
        var actions = new Dictionary<string, ExplanationDocument>(StringComparer.OrdinalIgnoreCase);
        var terms = new Dictionary<string, GlossaryTerm>(StringComparer.OrdinalIgnoreCase);

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) continue;

            var text = ReadResource(assembly, resourceName);

            // Resource names are dotted paths such as GitHelper.Content.actions.stage_file.md
            if (resourceName.Contains(".actions.", StringComparison.OrdinalIgnoreCase))
            {
                var document = ContentParser.Parse(text, resourceName);
                if (actions.ContainsKey(document.Id))
                    throw new ContentException($"{resourceName}: duplicate action id '{document.Id}'.");
                actions[document.Id] = document;
            }
            else if (resourceName.Contains(".terms.", StringComparison.OrdinalIgnoreCase))
            {
                var term = ParseTerm(text, resourceName);
                if (terms.ContainsKey(term.Id))
                    throw new ContentException($"{resourceName}: duplicate term id '{term.Id}'.");
                terms[term.Id] = term;
            }
        }

        return new ContentLibrary(actions, terms);
    }

    private static string ReadResource(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new ContentException($"{name}: embedded resource could not be opened.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Glossary files share the frontmatter format but have a single '## definition'
    /// section, so they are parsed here rather than by the action-shaped ContentParser.
    /// </summary>
    private static GlossaryTerm ParseTerm(string text, string sourceName)
    {
        var normalized = text.Replace("\r\n", "\n");

        string? id = null;
        string? title = null;
        var marker = "\n---";
        var end = normalized.IndexOf(marker, 3, StringComparison.Ordinal);
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal) || end < 0)
            throw new ContentException($"{sourceName}: term file must begin with '---' frontmatter.");

        foreach (var line in normalized[4..end].Split('\n'))
        {
            var separator = line.IndexOf(':');
            if (separator < 0) continue;
            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (key.Equals("id", StringComparison.OrdinalIgnoreCase)) id = value;
            else if (key.Equals("title", StringComparison.OrdinalIgnoreCase)) title = value;
        }

        if (string.IsNullOrWhiteSpace(id))
            throw new ContentException($"{sourceName}: term frontmatter is missing 'id'.");
        if (string.IsNullOrWhiteSpace(title))
            throw new ContentException($"{sourceName}: term frontmatter is missing 'title'.");

        var bodyStart = normalized.IndexOf('\n', end + 1);
        var body = bodyStart < 0 ? string.Empty : normalized[(bodyStart + 1)..];

        var headingIndex = body.IndexOf("## definition", StringComparison.OrdinalIgnoreCase);
        if (headingIndex < 0)
            throw new ContentException($"{sourceName}: missing required section '## definition'.");

        var definitionText = body[(headingIndex + "## definition".Length)..];
        var definition = ContentParser.ParseBlocksForTerm(definitionText);
        if (definition.Count == 0)
            throw new ContentException($"{sourceName}: '## definition' section is empty.");

        return new GlossaryTerm(id!, title!, definition);
    }
}
```

- [ ] **Step 8: Expose block parsing to ContentLibrary**

Add to `src/GitHelper.Core/Content/ContentParser.cs`, inside the class:

```csharp
    /// <summary>
    /// Block parsing for glossary files, which have their own section shape but the
    /// same inline and block syntax.
    /// </summary>
    internal static IReadOnlyList<ContentBlock> ParseBlocksForTerm(string sectionText)
        => ParseBlocks(sectionText);
```

Because `ContentLibrary` lives in the same assembly, `internal` is sufficient and keeps the general block parser off the public surface.

- [ ] **Step 9: Write SlotBinder**

Create `src/GitHelper.Core/Content/SlotBinder.cs`:

```csharp
using GitHelper.Core.Model;

namespace GitHelper.Core.Content;

/// <summary>Fills the {slot} placeholders in authored content from live repository state.</summary>
public static class SlotBinder
{
    /// <summary>How many filenames are listed before the remainder is summarised.</summary>
    private const int FileListLimit = 3;

    /// <summary>
    /// The closed vocabulary. Content referencing a slot outside this set is a content
    /// error caught by the Task 10 tests, never a placeholder left visible at runtime.
    /// </summary>
    public static IReadOnlySet<string> KnownSlots { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "branch", "upstream", "ahead", "behind",
        "stagedCount", "unstagedCount", "untrackedCount",
        "stagedFileList", "path", "branchName", "repoName",
    };

    public static IReadOnlyDictionary<string, string> Bind(
        RepoState state,
        string? path = null,
        string? branchName = null)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["branch"] = state.Branch ?? "no branch (detached)",
            ["upstream"] = state.Upstream ?? "no upstream branch",
            ["ahead"] = state.Ahead.ToString(),
            ["behind"] = state.Behind.ToString(),
            ["stagedCount"] = state.Staged.Count.ToString(),
            ["unstagedCount"] = state.Unstaged.Count.ToString(),
            ["untrackedCount"] = state.Untracked.Count.ToString(),
            ["stagedFileList"] = Summarise(state.Staged.Select(c => c.Path)),
            ["path"] = path ?? "this file",
            ["branchName"] = branchName ?? "the branch",
            ["repoName"] = new DirectoryInfo(state.RepoRoot).Name,
        };
    }

    /// <summary>Lists a few names, then says how many remain, so a panel never becomes a wall of paths.</summary>
    private static string Summarise(IEnumerable<string> paths)
    {
        var all = paths.ToList();
        if (all.Count == 0) return "no files";

        var shown = string.Join(", ", all.Take(FileListLimit));
        var remaining = all.Count - FileListLimit;
        return remaining > 0 ? $"{shown}, and {remaining} more" : shown;
    }
}
```

- [ ] **Step 10: Run the tests**

Run: `dotnet test --filter "ContentLibraryTests|SlotBinderTests"`
Expected: PASS, 9 tests.

- [ ] **Step 11: Run the whole suite**

Run: `dotnet test`
Expected: PASS, everything from Tasks 1–7.

- [ ] **Step 12: Commit**

```bash
git add -A
git commit -m "feat: load embedded content and bind slots from repository state"
```

---

### Task 8: Action descriptors and preconditions

**Files:**
- Create: `src/GitHelper.Core/Actions/ActionRequest.cs`
- Create: `src/GitHelper.Core/Actions/GitAction.cs`
- Create: `src/GitHelper.Core/Actions/Preconditions.cs`
- Test: `tests/GitHelper.Core.Tests/PreconditionTests.cs`

**Interfaces:**
- Consumes: `RepoState`, `FileChange`, `ChangeKind` (Tasks 3 and 5); `Danger` (Task 6).
- Produces:
  - `ActionRequest(string ActionId, string? Path = null, string? Message = null, string? BranchName = null)`
  - `PreconditionResult(bool Satisfied, string? Message = null, string? SuggestedActionId = null)` with `PreconditionResult.Ok` and `PreconditionResult.Fail(message, suggestedActionId = null)`
  - `interface IPrecondition { PreconditionResult Evaluate(RepoState state, ActionRequest request); }`
  - `GitAction(string Id, string Title, Danger Danger, Func<RepoState, ActionRequest, IReadOnlyList<string>> BuildArgs, IReadOnlyList<IPrecondition> Preconditions, string? UndoActionId = null)` with `string ExplanationId => Id`
  - Precondition types: `RequiresPath`, `RequiresMessage`, `RequiresBranchName`, `RequiresCommits`, `RequiresParentCommit`, `RequiresStagedChanges`, `RequiresRemote`, `RequiresUpstream`, `RequiresNoUncommittedChanges`, `RequiresNotCurrentBranch`, `RequiresBranchDoesNotExist`

**The content id convention.** `GitAction.ExplanationId` returns `Id`. The action id and its content filename are the same string by convention rather than by a separate field, which removes a whole class of mismatch. Task 10 asserts every action id resolves to a content file.

**Precondition messages are teaching copy, not error text.** Each failure message explains the underlying git concept in plain English and, where there is a sensible next move, names a `SuggestedActionId` the UI can offer as a button. These strings are user-facing.

- [ ] **Step 1: Write the failing test**

Create `tests/GitHelper.Core.Tests/PreconditionTests.cs`:

```csharp
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
            Branches: new[] { new BranchInfo("main", "origin/main"), new BranchInfo("feature", null) });

    private static ActionRequest Request(
        string? path = null, string? message = null, string? branchName = null)
        => new("test", path, message, branchName);

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
        };

        // A state and request chosen so that every precondition above fails.
        var failing = State(
            branch: "main", upstream: null, hasRemote: false, hasCommits: false, commitCount: 1,
            changes: new FileChange("a.txt", null, ChangeKind.None, ChangeKind.Modified));
        var request = Request(branchName: "main");

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
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter PreconditionTests`
Expected: FAIL — none of the precondition types exist (CS0246).

- [ ] **Step 3: Write ActionRequest**

Create `src/GitHelper.Core/Actions/ActionRequest.cs`:

```csharp
namespace GitHelper.Core.Actions;

/// <summary>
/// Names an action and its parameters. The UI never builds a git command; it names an
/// action and supplies these values.
/// </summary>
public sealed record ActionRequest(
    string ActionId,
    string? Path = null,
    string? Message = null,
    string? BranchName = null);
```

- [ ] **Step 4: Write GitAction**

Create `src/GitHelper.Core/Actions/GitAction.cs`:

```csharp
using GitHelper.Core.Model;

namespace GitHelper.Core.Actions;

/// <summary>
/// One action, expressed as data. Adding an action is a descriptor plus a content file,
/// with no new UI code and no new branches in the preview/run flow.
/// </summary>
public sealed record GitAction(
    string Id,
    string Title,
    Danger Danger,
    Func<RepoState, ActionRequest, IReadOnlyList<string>> BuildArgs,
    IReadOnlyList<IPrecondition> Preconditions,
    string? UndoActionId = null)
{
    /// <summary>
    /// The content file id, equal to the action id by convention. Keeping these the same
    /// string rather than two fields removes a whole class of mismatch.
    /// </summary>
    public string ExplanationId => Id;
}
```

- [ ] **Step 5: Write the preconditions**

Create `src/GitHelper.Core/Actions/Preconditions.cs`:

```csharp
using GitHelper.Core.Model;

namespace GitHelper.Core.Actions;

/// <summary>
/// The outcome of one precondition. Failure messages are user-facing teaching copy:
/// they explain the underlying git concept rather than restating the obstacle.
/// </summary>
public sealed record PreconditionResult(
    bool Satisfied,
    string? Message = null,
    string? SuggestedActionId = null)
{
    public static readonly PreconditionResult Ok = new(true);

    public static PreconditionResult Fail(string message, string? suggestedActionId = null)
        => new(false, message, suggestedActionId);
}

public interface IPrecondition
{
    PreconditionResult Evaluate(RepoState state, ActionRequest request);
}

public sealed class RequiresPath : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
        => string.IsNullOrWhiteSpace(request.Path)
            ? PreconditionResult.Fail("Pick a file first — this action works on one file at a time.")
            : PreconditionResult.Ok;
}

public sealed class RequiresMessage : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
        => string.IsNullOrWhiteSpace(request.Message)
            ? PreconditionResult.Fail(
                "Every save needs a short description so you can recognise it later. "
                + "A few words about what you changed is enough.")
            : PreconditionResult.Ok;
}

public sealed class RequiresBranchName : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
        => string.IsNullOrWhiteSpace(request.BranchName)
            ? PreconditionResult.Fail("Type a name for the branch.")
            : PreconditionResult.Ok;
}

public sealed class RequiresCommits : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
        => state.HasCommits
            ? PreconditionResult.Ok
            : PreconditionResult.Fail(
                "This project has no saved versions yet. Make your first commit before doing this.",
                "commit");
}

public sealed class RequiresParentCommit : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
        => state.CanUndoLastCommit
            ? PreconditionResult.Ok
            : PreconditionResult.Fail(
                "This is the first commit in the project, so there is no earlier version to step "
                + "back to. Undoing it this way is not possible.");
}

public sealed class RequiresStagedChanges : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
        => state.HasStagedChanges
            ? PreconditionResult.Ok
            : PreconditionResult.Fail(
                "Nothing is staged yet. Editing a file is not the same as choosing it: you pick "
                + "which changes go into a commit by staging them first.",
                "stage-all");
}

public sealed class RequiresRemote : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
        => state.HasRemote
            ? PreconditionResult.Ok
            : PreconditionResult.Fail(
                "This project has no online copy configured, so there is nowhere to send changes "
                + "to or fetch them from.");
}

public sealed class RequiresUpstream : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
        => state.Upstream is not null
            ? PreconditionResult.Ok
            : PreconditionResult.Fail(
                "This branch is not linked to a branch on the server yet, so git does not know "
                + "where to get changes from. Pushing once will set that link up.",
                "push");
}

public sealed class RequiresNoUncommittedChanges : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
    {
        // Untracked files are carried across a switch untouched, so they are not an obstacle.
        var blocking = state.Changes.Any(c => c.IsStaged || c.HasUnstagedChanges);

        return blocking
            ? PreconditionResult.Fail(
                "You have changes that are not saved yet. Switching now could mix them into "
                + "another branch, so commit them first.",
                "commit")
            : PreconditionResult.Ok;
    }
}

public sealed class RequiresNotCurrentBranch : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
        => string.Equals(request.BranchName, state.Branch, StringComparison.Ordinal)
            ? PreconditionResult.Fail(
                "You are on this branch right now. Switch to a different branch before deleting it.")
            : PreconditionResult.Ok;
}

public sealed class RequiresBranchDoesNotExist : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
        => state.Branches.Any(b => string.Equals(b.Name, request.BranchName, StringComparison.Ordinal))
            ? PreconditionResult.Fail(
                $"A branch called '{request.BranchName}' already exists. Pick a different name.")
            : PreconditionResult.Ok;
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test --filter PreconditionTests`
Expected: PASS, 14 tests.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: add action descriptors and teaching preconditions"
```

---

### Task 9: The action catalog

**Files:**
- Create: `src/GitHelper.Core/Actions/ActionCatalog.cs`
- Test: `tests/GitHelper.Core.Tests/ActionCatalogTests.cs`

**Interfaces:**
- Consumes: `GitAction`, `ActionRequest`, `Danger`, all precondition types (Task 8); `RepoState` (Task 5); `IGitRunner`, `TestRepo` (Task 2).
- Produces:
  - `ActionCatalog.All -> IReadOnlyList<GitAction>`
  - `ActionCatalog.Find(string actionId) -> GitAction?`
  - Action ids, fixed for the rest of the project: `stage-file`, `unstage-file`, `stage-all`, `unstage-all`, `commit`, `create-branch`, `switch-branch`, `fetch`, `pull`, `push`, `discard-file`, `undo-last-commit`, `delete-branch`.

**Two argv rules that carry real weight:**

- **`--` before any path.** Without it, a file named `-f` or `main` would be read by git as a flag or a revision. Every path-taking action includes it.
- **Empty-repository fallback for unstaging.** `git restore --staged` needs a HEAD to restore from and fails in a repository with no commits. When `state.HasCommits` is false, the descriptor emits `git rm --cached` instead. This is why `BuildArgs` receives `RepoState` rather than only the request.

**Two teaching decisions carried from the spec:**

- **`pull --ff-only`.** A beginner must never get a merge commit they did not ask for and cannot explain. When the pull cannot fast-forward, it refuses and the error translator (Task 11) explains why.
- **`branch -d`, never `-D`.** The safe form refuses to delete a branch holding unmerged work. That refusal is explained, not overridden.

- [ ] **Step 1: Write the failing test**

Create `tests/GitHelper.Core.Tests/ActionCatalogTests.cs`:

```csharp
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
    public void All_ContainsExactlyTheThirteenV1Actions()
    {
        var expected = new[]
        {
            "stage-file", "unstage-file", "stage-all", "unstage-all", "commit",
            "create-branch", "switch-branch", "fetch", "pull", "push",
            "discard-file", "undo-last-commit", "delete-branch",
        };

        Assert.Equal(expected.OrderBy(x => x), ActionCatalog.All.Select(a => a.Id).OrderBy(x => x));
    }

    [Fact]
    public void DiscardFile_IsTheOnlyDestructiveActionInV1()
    {
        var destructive = ActionCatalog.All.Where(a => a.Danger == Danger.Destructive).Select(a => a.Id);

        Assert.Equal(new[] { "discard-file" }, destructive);
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
            Array.Empty<FileChange>(), Array.Empty<CommitInfo>(), Array.Empty<BranchInfo>());

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
            Array.Empty<FileChange>(), Array.Empty<CommitInfo>(), Array.Empty<BranchInfo>());

        var args = ActionCatalog.Find("pull")!.BuildArgs(state, new ActionRequest("pull"));

        Assert.Contains("--ff-only", args);
    }

    [Fact]
    public void DeleteBranch_NeverForceDeletes()
    {
        var state = new RepoState(
            @"C:\r", "main", false, null, 0, 0, true, false,
            Array.Empty<FileChange>(), Array.Empty<CommitInfo>(), Array.Empty<BranchInfo>());

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
            Array.Empty<FileChange>(), Array.Empty<CommitInfo>(), Array.Empty<BranchInfo>());
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
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter ActionCatalogTests`
Expected: FAIL — `ActionCatalog` does not exist (CS0246).

- [ ] **Step 3: Write ActionCatalog**

Create `src/GitHelper.Core/Actions/ActionCatalog.cs`:

```csharp
using GitHelper.Core.Model;

namespace GitHelper.Core.Actions;

/// <summary>The v1 action set, expressed as data.</summary>
public static class ActionCatalog
{
    private static readonly IPrecondition[] None = Array.Empty<IPrecondition>();

    public static IReadOnlyList<GitAction> All { get; } = new[]
    {
        new GitAction(
            Id: "stage-file",
            Title: "Stage file",
            Danger: Danger.Safe,
            BuildArgs: (_, r) => new[] { "add", "--", r.Path! },
            Preconditions: new IPrecondition[] { new RequiresPath() },
            UndoActionId: "unstage-file"),

        new GitAction(
            Id: "unstage-file",
            Title: "Unstage file",
            Danger: Danger.Safe,
            // restore --staged needs a HEAD; before the first commit there is none.
            BuildArgs: (s, r) => s.HasCommits
                ? new[] { "restore", "--staged", "--", r.Path! }
                : new[] { "rm", "--cached", "--", r.Path! },
            Preconditions: new IPrecondition[] { new RequiresPath() },
            UndoActionId: "stage-file"),

        new GitAction(
            Id: "stage-all",
            Title: "Stage everything",
            Danger: Danger.Safe,
            BuildArgs: (_, _) => new[] { "add", "-A" },
            Preconditions: None,
            UndoActionId: "unstage-all"),

        new GitAction(
            Id: "unstage-all",
            Title: "Unstage everything",
            Danger: Danger.Safe,
            BuildArgs: (s, _) => s.HasCommits
                ? new[] { "restore", "--staged", "--", "." }
                : new[] { "rm", "--cached", "-r", "--", "." },
            Preconditions: None,
            UndoActionId: "stage-all"),

        new GitAction(
            Id: "commit",
            Title: "Commit",
            Danger: Danger.Caution,
            BuildArgs: (_, r) => new[] { "commit", "-m", r.Message! },
            Preconditions: new IPrecondition[] { new RequiresMessage(), new RequiresStagedChanges() },
            UndoActionId: "undo-last-commit"),

        new GitAction(
            Id: "create-branch",
            Title: "Create branch",
            Danger: Danger.Safe,
            BuildArgs: (_, r) => new[] { "switch", "-c", r.BranchName! },
            Preconditions: new IPrecondition[]
            {
                new RequiresBranchName(), new RequiresCommits(), new RequiresBranchDoesNotExist(),
            }),

        new GitAction(
            Id: "switch-branch",
            Title: "Switch branch",
            Danger: Danger.Caution,
            BuildArgs: (_, r) => new[] { "switch", r.BranchName! },
            Preconditions: new IPrecondition[]
            {
                new RequiresBranchName(), new RequiresNoUncommittedChanges(),
            }),

        new GitAction(
            Id: "fetch",
            Title: "Check for updates",
            Danger: Danger.Safe,
            BuildArgs: (_, _) => new[] { "fetch" },
            Preconditions: new IPrecondition[] { new RequiresRemote() }),

        new GitAction(
            Id: "pull",
            Title: "Get changes from the server",
            Danger: Danger.Caution,
            // --ff-only: refuse rather than silently create a merge commit the user
            // did not ask for and could not explain.
            BuildArgs: (_, _) => new[] { "pull", "--ff-only" },
            Preconditions: new IPrecondition[] { new RequiresRemote(), new RequiresUpstream() }),

        new GitAction(
            Id: "push",
            Title: "Send changes to the server",
            Danger: Danger.Caution,
            BuildArgs: (s, _) => s.Upstream is null
                ? new[] { "push", "--set-upstream", "origin", s.Branch! }
                : new[] { "push" },
            Preconditions: new IPrecondition[] { new RequiresRemote(), new RequiresCommits() }),

        new GitAction(
            Id: "discard-file",
            Title: "Discard changes to file",
            Danger: Danger.Destructive,
            BuildArgs: (_, r) => new[] { "restore", "--", r.Path! },
            Preconditions: new IPrecondition[] { new RequiresPath() }),

        new GitAction(
            Id: "undo-last-commit",
            Title: "Undo last commit",
            Danger: Danger.Caution,
            // --soft: the commit is removed but the work stays, staged and safe.
            BuildArgs: (_, _) => new[] { "reset", "--soft", "HEAD~1" },
            Preconditions: new IPrecondition[] { new RequiresCommits(), new RequiresParentCommit() }),

        new GitAction(
            Id: "delete-branch",
            Title: "Delete branch",
            Danger: Danger.Caution,
            // -d, never -D: git refuses to delete a branch holding unmerged work,
            // and that refusal is explained rather than overridden.
            BuildArgs: (_, r) => new[] { "branch", "-d", r.BranchName! },
            Preconditions: new IPrecondition[]
            {
                new RequiresBranchName(), new RequiresNotCurrentBranch(),
            }),
    };

    private static readonly Dictionary<string, GitAction> ById =
        All.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);

    public static GitAction? Find(string actionId)
        => ById.TryGetValue(actionId, out var action) ? action : null;
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test --filter ActionCatalogTests`
Expected: PASS, 16 tests.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add the thirteen v1 action descriptors"
```

---

### Task 10: Author all content and enforce it with tests

**Files:**
- Create: `src/GitHelper.Content/terms/*.md` (7 files; `staging-area.md` already exists)
- Create: `src/GitHelper.Content/actions/*.md` (12 files)
- Modify: `src/GitHelper.Content/actions/stage-file.md` (add the `undo:` field)
- Test: `tests/GitHelper.Core.Tests/ContentIntegrityTests.cs`

**Interfaces:**
- Consumes: `ContentLibrary`, `SlotBinder`, `ContentBlock` (Tasks 6 and 7); `ActionCatalog`, `Danger` (Tasks 8 and 9).
- Produces: no new types. Produces the guarantee that content and code cannot drift apart.

**This task is where the spec's content-correctness rule is made real.** Content mistakes must be red tests, never blank panels at runtime. Write the tests first: they will fail against the single existing content file, and authoring the rest is what turns them green.

The four required checks, plus two more that fell out of the design:

1. Every action id in `ActionCatalog` has a content file.
2. Every `terms:` reference resolves to a glossary file, including terms used inline as `[[...]]`.
3. Every `{slot}` used is in `SlotBinder.KnownSlots`.
4. Every `Destructive` action has a non-empty `## undo` section.
5. The `danger:` in frontmatter matches the descriptor's `Danger`.
6. The `undo:` in frontmatter matches the descriptor's `UndoActionId`.
7. No orphaned content: every content file corresponds to a real action.
8. No unused glossary terms.

Checks 5 and 6 exist because those two values are stated in both the descriptor and the
frontmatter. Rather than removing one, the tests make drift between them impossible —
the frontmatter is what a content author reads, and it should be true.

- [ ] **Step 1: Write the failing tests**

Create `tests/GitHelper.Core.Tests/ContentIntegrityTests.cs`:

```csharp
using GitHelper.Core.Actions;
using GitHelper.Core.Content;

namespace GitHelper.Core.Tests;

/// <summary>
/// Keeps authored content and code from drifting apart. Every failure here is a content
/// bug that would otherwise surface as a blank or wrong explanation panel.
/// </summary>
public class ContentIntegrityTests
{
    private static readonly ContentLibrary Library = ContentLibrary.Load();

    private static IEnumerable<InlineSpan> AllSpans(ExplanationDocument document)
        => Spans(document.What).Concat(Spans(document.Risks)).Concat(Spans(document.Undo));

    private static IEnumerable<InlineSpan> Spans(IEnumerable<ContentBlock> blocks)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    foreach (var span in paragraph.Spans) yield return span;
                    break;
                case BulletListBlock bullets:
                    foreach (var item in bullets.Items)
                        foreach (var span in item) yield return span;
                    break;
            }
        }
    }

    [Fact]
    public void EveryActionHasAContentFile()
    {
        var missing = ActionCatalog.All
            .Where(a => !Library.Actions.ContainsKey(a.ExplanationId))
            .Select(a => a.Id)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void EveryContentFileMatchesARealAction()
    {
        var orphans = Library.Actions.Keys
            .Where(id => ActionCatalog.Find(id) is null)
            .ToList();

        Assert.Empty(orphans);
    }

    [Fact]
    public void EveryDeclaredTermResolvesToAGlossaryFile()
    {
        var unresolved = Library.Actions.Values
            .SelectMany(d => d.Terms.Select(t => (Document: d.Id, Term: t)))
            .Where(x => !Library.Terms.ContainsKey(x.Term))
            .ToList();

        Assert.Empty(unresolved);
    }

    [Fact]
    public void EveryInlineTermReferenceResolvesToAGlossaryFile()
    {
        var unresolved = Library.Actions.Values
            .SelectMany(d => AllSpans(d).OfType<TermSpan>().Select(s => (Document: d.Id, s.TermId)))
            .Where(x => !Library.Terms.ContainsKey(x.TermId))
            .ToList();

        Assert.Empty(unresolved);
    }

    [Fact]
    public void EverySlotIsInTheKnownVocabulary()
    {
        var unknown = Library.Actions.Values
            .SelectMany(d => AllSpans(d).OfType<SlotSpan>().Select(s => (Document: d.Id, s.SlotName)))
            .Where(x => !SlotBinder.KnownSlots.Contains(x.SlotName))
            .ToList();

        Assert.Empty(unknown);
    }

    [Fact]
    public void EveryDestructiveActionExplainsHowToUndoIt()
    {
        foreach (var action in ActionCatalog.All.Where(a => a.Danger == Danger.Destructive))
        {
            var document = Library.Actions[action.ExplanationId];
            Assert.NotEmpty(document.Undo);
        }
    }

    [Fact]
    public void EveryActionExplainsWhatItDoesAndWhatCouldGoWrong()
    {
        foreach (var action in ActionCatalog.All)
        {
            var document = Library.Actions[action.ExplanationId];
            Assert.NotEmpty(document.What);
            Assert.NotEmpty(document.Risks);
            Assert.NotEmpty(document.Undo);
        }
    }

    [Fact]
    public void FrontmatterDangerMatchesTheActionDescriptor()
    {
        foreach (var action in ActionCatalog.All)
        {
            var document = Library.Actions[action.ExplanationId];
            Assert.Equal(action.Danger, document.Danger);
        }
    }

    [Fact]
    public void FrontmatterUndoMatchesTheActionDescriptor()
    {
        foreach (var action in ActionCatalog.All)
        {
            var document = Library.Actions[action.ExplanationId];
            Assert.Equal(action.UndoActionId, document.UndoActionId);
        }
    }

    [Fact]
    public void EveryGlossaryTermIsActuallyReferencedSomewhere()
    {
        var referenced = Library.Actions.Values
            .SelectMany(d => d.Terms.Concat(AllSpans(d).OfType<TermSpan>().Select(s => s.TermId)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unused = Library.Terms.Keys.Where(id => !referenced.Contains(id)).ToList();

        Assert.Empty(unused);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter ContentIntegrityTests`
Expected: FAIL — `EveryActionHasAContentFile` reports twelve missing ids, since only `stage-file` exists.

- [ ] **Step 3: Author the glossary**

Create these seven files under `src/GitHelper.Content/terms/`. `staging-area.md` already exists from Task 7.

`commit.md`:

```markdown
---
id: commit
title: commit
---
## definition
One saved version of your project, with a short note about what changed. Commits are
kept forever and stack up into a history you can look back through.
```

`branch.md`:

```markdown
---
id: branch
title: branch
---
## definition
A separate line of work. Changes you make on one branch do not affect the others, so
you can try something out without disturbing work that already works.
```

`working-directory.md`:

```markdown
---
id: working-directory
title: working files
---
## definition
The files as they are on your computer right now, including edits you have not saved
into a commit yet. This is the only place unsaved edits exist.
```

`upstream.md`:

```markdown
---
id: upstream
title: upstream branch
---
## definition
The branch on the server that your local branch is paired with. The pairing is what
lets git tell you that you are ahead or behind, and where to send your work.
```

`remote.md`:

```markdown
---
id: remote
title: remote
---
## definition
A copy of the project stored somewhere else, usually on a server such as GitHub. It is
what lets you back up your work and share it with other people.
```

`fast-forward.md`:

```markdown
---
id: fast-forward
title: fast-forward
---
## definition
The simple case when getting changes: you have made nothing new, so the server's
commits can just be added on top of yours. When both sides have new commits, a
fast-forward is not possible and the two histories have to be combined instead.
```

`unmerged-branch.md`:

```markdown
---
id: unmerged-branch
title: unmerged work
---
## definition
Commits that exist only on one branch and have not been copied anywhere else. Deleting
that branch would be the only way to lose them, which is why git refuses to do it
without being asked twice.
```

- [ ] **Step 4: Run the tests again**

Run: `dotnet test --filter ContentIntegrityTests`
Expected: still FAIL — the action files do not exist yet, and `EveryGlossaryTermIsActuallyReferencedSomewhere` now fails too because nothing references the new terms. Both are fixed by the next step.

---

- [ ] **Step 5: Correct the existing stage-file frontmatter**

`stage-file.md` was authored in Task 7 before the catalog existed, so its frontmatter has no `undo:` while the descriptor sets `UndoActionId: "unstage-file"`. Add the line, so `FrontmatterUndoMatchesTheActionDescriptor` passes:

```markdown
---
id: stage-file
title: Stage file
danger: safe
terms: [staging-area]
undo: unstage-file
---
```

Leave the body of the file unchanged.

- [ ] **Step 6: Author the twelve remaining action files**

Create these under `src/GitHelper.Content/actions/`. Every file uses only the slots in `SlotBinder.KnownSlots` and only the term ids authored in Step 3.

`unstage-file.md`:

```markdown
---
id: unstage-file
title: Unstage file
danger: safe
terms: [staging-area]
undo: stage-file
---
## what
Takes {path} back out of the [[staging-area]]. Your edits to the file are untouched —
you are only saying you do not want this file in your next [[commit]].

## risks
None. Nothing you have written is changed or lost.

## undo
Stage the file again.
```

`stage-all.md`:

```markdown
---
id: stage-all
title: Stage everything
danger: safe
terms: [staging-area]
undo: unstage-all
---
## what
Puts every change you have made into the [[staging-area]], including files git has not
seen before. You currently have {unstagedCount} edited and {untrackedCount} new files.

## risks
It is easy to include something you did not mean to, such as a private file or a
scratch note. Look over the list before you commit.

## undo
Unstage everything, then pick files one at a time.
```

`unstage-all.md`:

```markdown
---
id: unstage-all
title: Unstage everything
danger: safe
terms: [staging-area]
undo: stage-all
---
## what
Empties the [[staging-area]], so none of your {stagedCount} staged changes are lined up
for the next [[commit]]. Your edits stay exactly as they are.

## risks
None. This never changes the contents of your files.

## undo
Stage the files again.
```

`commit.md`:

```markdown
---
id: commit
title: Commit
danger: caution
terms: [commit, staging-area, branch]
undo: undo-last-commit
---
## what
Saves the {stagedCount} staged file(s) as a new [[commit]] on [[branch|branch]] {branch}.
This is the point at which your work becomes part of the permanent history.

Staged files: {stagedFileList}

## risks
Only staged changes are saved. Anything you edited but did not stage stays unsaved, so
check that nothing you wanted is being left behind.

The description you write is kept forever and is what you will scan through later, so a
few specific words beat "update".

## undo
Undo the last commit. The commit disappears and all the work comes back, staged and safe.
```

`create-branch.md`:

```markdown
---
id: create-branch
title: Create branch
danger: safe
terms: [branch, commit]
---
## what
Starts a new [[branch]] called {branchName} from where you are now, and moves you onto
it. Commits you make from here will not affect {branch}.

## risks
None. Nothing is copied, moved, or deleted — a branch is just a name pointing at a
[[commit]]. Any edits you have in progress come with you.

## undo
Switch back to {branch}, then delete the new branch.
```

`switch-branch.md`:

```markdown
---
id: switch-branch
title: Switch branch
danger: caution
terms: [branch, working-directory]
---
## what
Moves you to [[branch|branch]] {branchName} and replaces your [[working-directory|working files]]
with that branch's versions.

## risks
Files on screen will change. That is expected, not a bug — you are looking at a
different version of the project.

If you have unsaved edits, git will refuse rather than risk mixing them into the other
branch. Commit first.

## undo
Switch back to {branch}. Nothing is lost by moving between branches.
```

`fetch.md`:

```markdown
---
id: fetch
title: Check for updates
danger: safe
terms: [remote, branch]
---
## what
Asks the [[remote|server]] what has changed, and downloads that information. It does
**not** touch your files or your [[branch]] — it only updates what git knows.

## risks
None whatsoever. This is the safest thing you can do, and it is a good habit before
starting work.

## undo
Nothing to undo; nothing changed on your computer.
```

`pull.md`:

```markdown
---
id: pull
title: Get changes from the server
danger: caution
terms: [remote, upstream, fast-forward, commit]
---
## what
Downloads new commits from {upstream} and adds them to your branch. You are currently
{behind} commit(s) behind.

This app only does the simple case, called a [[fast-forward]]: the server's work is
placed on top of yours. If both you and someone else have made commits, git will stop
and tell you rather than combining the two histories on its own.

## risks
If it refuses, nothing has happened and nothing is broken — it means both sides have
new work, and combining them is a decision you should make deliberately.

## undo
There is nothing to undo when it refuses. When it succeeds, you have simply received
[[commit|commits]] other people already made.
```

`push.md`:

```markdown
---
id: push
title: Send changes to the server
danger: caution
terms: [remote, upstream, commit, branch]
---
## what
Uploads your {ahead} unsent [[commit|commit(s)]] from [[branch|branch]] {branch} to the
[[remote|server]]. Once this succeeds, your work is backed up and other people can see it.

If this branch has no [[upstream]] yet, this also sets one up, so future sends know
where to go.

## risks
What you send becomes visible to everyone with access to the project, so it is worth a
look at what is in your commits first.

If someone else has pushed work you do not have, git will refuse. Get their changes
first, then send yours.

## undo
Sending cannot be taken back from inside this app. Anyone may already have downloaded
it, so the normal fix is a new commit that corrects the problem.
```

`discard-file.md`:

```markdown
---
id: discard-file
title: Discard changes to file
danger: destructive
terms: [working-directory, commit]
---
## what
Throws away every edit you have made to {path} since your last [[commit]], and puts the
file back to that saved version.

## risks
**This permanently deletes your unsaved edits to this file.** They were never committed,
so git has no copy of them anywhere. Nothing in this app or in git can bring them back.

If you are unsure, commit first instead — you can always undo a commit.

## undo
There is no undo. This is the one action in this app that can lose work for good, which
is why it asks you twice.
```

`undo-last-commit.md`:

```markdown
---
id: undo-last-commit
title: Undo last commit
danger: caution
terms: [commit, staging-area]
---
## what
Removes your most recent [[commit]] from the history, but keeps everything it contained
as staged changes in the [[staging-area]]. Nothing you wrote is lost — the save is
undone, not the work.

Use this when you committed too early, used the wrong message, or left a file out.

## risks
If the commit was already sent to the server, undoing it locally puts you out of step
with what is there, which causes trouble on your next send.

The very first commit in a project cannot be undone this way, as there is no earlier
version to step back to.

## undo
Commit again. The changes are still staged and ready.
```

`delete-branch.md`:

```markdown
---
id: delete-branch
title: Delete branch
danger: caution
terms: [branch, unmerged-branch, commit]
---
## what
Deletes the [[branch]] {branchName}. The branch name goes away; the [[commit|commits]]
on it are not deleted by this.

## risks
Git checks first. If the branch holds [[unmerged-branch|unmerged work]] that exists
nowhere else, it refuses rather than deleting it — this app never overrides that
refusal, so a branch with work on it cannot be lost here by accident.

You cannot delete the branch you are currently on.

## undo
There is no undo button for this, but the work is not gone. Deleting a branch whose
commits live elsewhere loses nothing.
```

- [ ] **Step 7: Run the content tests**

Run: `dotnet test --filter ContentIntegrityTests`
Expected: PASS, 10 tests.

If `EveryInlineTermReferenceResolvesToAGlossaryFile` fails, a `[[...]]` reference has a typo — the failure message names the document and the id. If `EverySlotIsInTheKnownVocabulary` fails, a `{slot}` is not in `SlotBinder.KnownSlots`; either fix the typo or add the slot in `SlotBinder` along with the value that fills it.

- [ ] **Step 8: Run the whole suite**

Run: `dotnet test`
Expected: PASS, everything from Tasks 1–10.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: author all action and glossary content with integrity tests"
```

---

### Task 11: Error translation

**Files:**
- Create: `src/GitHelper.Core/Errors/TranslatedError.cs`
- Create: `src/GitHelper.Core/Errors/ErrorTranslator.cs`
- Test: `tests/GitHelper.Core.Tests/ErrorTranslatorTests.cs`

**Interfaces:**
- Consumes: `GitCommandResult` (Task 2).
- Produces:
  - `TranslatedError(string Summary, string Explanation, IReadOnlyList<string> NextSteps, string RawOutput, bool IsUnderstood)`
  - `ErrorTranslator.Translate(GitCommandResult result) -> TranslatedError?` (null when the command succeeded)

**Two presentation rules from the spec, both enforced by tests:**

1. **`RawOutput` is always populated**, on every translation including understood ones. The UI shows it behind "show technical details". A beginner who wants to search for the real message must be able to reach it, and concealing it makes the app untrustworthy the first time someone notices.
2. **Unmatched errors are admitted, not guessed.** They come back with `IsUnderstood = false` and a summary saying so. A teaching tool that invents plausible git explanations is worse than one that says it does not know.

Rules are ordered and first-match-wins, so put specific patterns before general ones. Matching is case-insensitive and looks at stderr and stdout together, because git splits messages across both depending on the subcommand.

- [ ] **Step 1: Write the failing test**

Create `tests/GitHelper.Core.Tests/ErrorTranslatorTests.cs`:

```csharp
using GitHelper.Core.Errors;
using GitHelper.Core.Git;

namespace GitHelper.Core.Tests;

public class ErrorTranslatorTests
{
    private static GitCommandResult Failure(string stdErr)
        => new(new[] { "push" }, StdOut: "", StdErr: stdErr, ExitCode: 1, Duration: TimeSpan.Zero);

    [Fact]
    public void Translate_ReturnsNullWhenTheCommandSucceeded()
    {
        var success = new GitCommandResult(new[] { "status" }, "", "", 0, TimeSpan.Zero);

        Assert.Null(ErrorTranslator.Translate(success));
    }

    [Theory]
    [InlineData("! [rejected]        main -> main (non-fast-forward)", "rejected")]
    [InlineData("fatal: The current branch main has no upstream branch.", "upstream")]
    [InlineData("fatal: not a git repository (or any of the parent directories): .git", "git project")]
    [InlineData("error: Your local changes to the following files would be overwritten by checkout:", "overwrite")]
    [InlineData("fatal: Authentication failed for 'https://example.com/repo.git/'", "sign in")]
    [InlineData("error: The branch 'feature' is not fully merged.", "nowhere else")]
    [InlineData("fatal: 'origin' does not appear to be a git repository", "server")]
    [InlineData("fatal: Not possible to fast-forward, aborting.", "fast-forward")]
    public void Translate_RecognisesKnownFailures(string stdErr, string expectedFragment)
    {
        var translated = ErrorTranslator.Translate(Failure(stdErr))!;

        Assert.True(translated.IsUnderstood);
        Assert.Contains(
            expectedFragment,
            translated.Summary + " " + translated.Explanation,
            StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(translated.NextSteps);
    }

    [Fact]
    public void Translate_AlwaysKeepsTheRawOutputReachable()
    {
        const string raw = "fatal: The current branch main has no upstream branch.";

        var translated = ErrorTranslator.Translate(Failure(raw))!;

        Assert.Contains(raw, translated.RawOutput);
    }

    [Fact]
    public void Translate_AdmitsIgnoranceRatherThanGuessing()
    {
        var translated = ErrorTranslator.Translate(Failure("fatal: something nobody has ever seen"))!;

        Assert.False(translated.IsUnderstood);
        Assert.Contains("plain-english", translated.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("something nobody has ever seen", translated.RawOutput);
    }

    [Fact]
    public void Translate_ReadsMessagesGitWroteToStdOut()
    {
        // git pull reports some refusals on stdout rather than stderr.
        var result = new GitCommandResult(
            new[] { "pull" },
            StdOut: "fatal: Not possible to fast-forward, aborting.",
            StdErr: "",
            ExitCode: 128,
            Duration: TimeSpan.Zero);

        Assert.True(ErrorTranslator.Translate(result)!.IsUnderstood);
    }

    [Fact]
    public void EveryRuleProducesNonEmptyUserFacingCopy()
    {
        string[] samples =
        {
            "! [rejected] main -> main (non-fast-forward)",
            "fatal: The current branch main has no upstream branch.",
            "fatal: not a git repository",
            "error: Your local changes to the following files would be overwritten by checkout:",
            "fatal: Authentication failed",
            "error: The branch 'feature' is not fully merged.",
            "fatal: 'origin' does not appear to be a git repository",
            "fatal: Not possible to fast-forward, aborting.",
            "nothing to commit, working tree clean",
        };

        foreach (var sample in samples)
        {
            var translated = ErrorTranslator.Translate(Failure(sample))!;
            Assert.False(string.IsNullOrWhiteSpace(translated.Summary));
            Assert.False(string.IsNullOrWhiteSpace(translated.Explanation));
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter ErrorTranslatorTests`
Expected: FAIL — `ErrorTranslator` and `TranslatedError` do not exist (CS0246).

- [ ] **Step 3: Write TranslatedError**

Create `src/GitHelper.Core/Errors/TranslatedError.cs`:

```csharp
namespace GitHelper.Core.Errors;

/// <summary>
/// A git failure, explained. <paramref name="RawOutput"/> is always populated, including
/// when the failure is understood — the UI shows it behind "show technical details" so a
/// user can always reach what git actually said.
/// </summary>
public sealed record TranslatedError(
    string Summary,
    string Explanation,
    IReadOnlyList<string> NextSteps,
    string RawOutput,
    bool IsUnderstood);
```

- [ ] **Step 4: Write ErrorTranslator**

Create `src/GitHelper.Core/Errors/ErrorTranslator.cs`:

```csharp
using GitHelper.Core.Git;

namespace GitHelper.Core.Errors;

/// <summary>Turns git's stderr into plain English, or admits when it cannot.</summary>
public static class ErrorTranslator
{
    private sealed record Rule(
        string Pattern,
        string Summary,
        string Explanation,
        string[] NextSteps);

    /// <summary>Ordered, first match wins. Specific patterns must precede general ones.</summary>
    private static readonly Rule[] Rules =
    {
        new("non-fast-forward",
            "The server has work you do not have yet",
            "Your send was rejected because someone else added commits to this branch after you "
            + "last got them. Git refuses rather than overwrite their work.",
            new[]
            {
                "Get the changes from the server first.",
                "Then send yours again.",
            }),

        new("no upstream branch",
            "This branch has no upstream branch on the server yet",
            "Git does not know which branch on the server this one belongs with, so it does not "
            + "know where to send your work. Sending once will set that link up.",
            new[] { "Send your changes; this app will set up the link at the same time." }),

        new("not a git repository",
            "This folder is not a git project",
            "Git keeps its history in a hidden .git folder, and there is not one here or in any "
            + "folder above it.",
            new[]
            {
                "Open a different folder.",
                "Or turn this folder into a git project first.",
            }),

        new("would be overwritten",
            "You have unsaved changes in the way",
            "Doing this would overwrite edits you have not committed, so git stopped instead of "
            + "losing them.",
            new[]
            {
                "Commit your changes, then try again.",
                "Or discard them if you do not want them.",
            }),

        new("authentication failed",
            "The server would not let you sign in",
            "Your saved sign-in details were refused. This app never handles your password — "
            + "Windows stores it for git in Credential Manager.",
            new[]
            {
                "Check that you still have access to this project.",
                "Update the saved credentials in Windows Credential Manager.",
            }),

        new("not fully merged",
            "That branch has work that exists nowhere else",
            "The branch holds commits that are not part of any other branch, so deleting it would "
            + "be the only way to lose them. Git refused on purpose.",
            new[]
            {
                "Look through the branch to see whether you still want that work.",
                "Merge it somewhere first if you do.",
            }),

        new("does not appear to be a git repository",
            "The server address does not work",
            "Git could not find a project at the address configured for this remote.",
            new[] { "Check the project address in your git settings." }),

        new("not possible to fast-forward",
            "Both you and the server have new work",
            "A fast-forward only works when you have made nothing new. Since both sides have "
            + "commits, the two histories have to be combined, which this app does not do yet.",
            new[]
            {
                "Save or set aside your local commits.",
                "Combining histories is not supported in this version.",
            }),

        new("nothing to commit",
            "There is nothing staged to save",
            "Editing a file is not the same as choosing it. You pick which changes go into a "
            + "commit by staging them first.",
            new[] { "Stage the files you want to save, then commit." }),

        new("pathspec",
            "Git could not find that file or branch",
            "The name given does not match a file or a branch that git knows about.",
            new[] { "Check the spelling, and that the file has not been moved or deleted." }),
    };

    public static TranslatedError? Translate(GitCommandResult result)
    {
        if (result.Success) return null;

        // git splits messages across both streams depending on the subcommand.
        var raw = (result.StdErr + "\n" + result.StdOut).Trim();

        foreach (var rule in Rules)
        {
            if (raw.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase))
                return new TranslatedError(
                    rule.Summary, rule.Explanation, rule.NextSteps, raw, IsUnderstood: true);
        }

        return new TranslatedError(
            Summary: "I don't have a plain-English explanation for this one",
            Explanation:
                "Git reported a problem this app does not recognise. The exact message is below — "
                + "searching the web for it usually finds an answer.",
            NextSteps: new[] { "Read the technical details below." },
            RawOutput: raw,
            IsUnderstood: false);
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test --filter ErrorTranslatorTests`
Expected: PASS, 13 tests.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: translate git failures into plain English without guessing"
```

---

### Task 12: ActionService and Narrator

**Files:**
- Create: `src/GitHelper.Core/Actions/Narrator.cs`
- Create: `src/GitHelper.Core/Actions/ActionPreview.cs`
- Create: `src/GitHelper.Core/Actions/ActionOutcome.cs`
- Create: `src/GitHelper.Core/Actions/ActionService.cs`
- Test: `tests/GitHelper.Core.Tests/NarratorTests.cs`
- Test: `tests/GitHelper.Core.Tests/ActionServiceTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 2, 5, 6, 7, 8, 9, 11.
- Produces:
  - `Narrator.Describe(RepoState before, RepoState after) -> string`
  - `ActionPreview(GitAction Action, IReadOnlyList<string> ArgVector, string CommandLine, ExplanationDocument Explanation, IReadOnlyDictionary<string, string> Slots, IReadOnlyList<PreconditionResult> Blockers, Danger Danger, string? UndoActionId)` with `bool CanRun => Blockers.Count == 0`
  - `ActionOutcome(bool Success, GitCommandResult Result, string? Narration, TranslatedError? Error, RepoState Before, RepoState After, IReadOnlyList<PreconditionResult> Blockers)`
  - `ActionService(IGitRunner runner, RepoStateReader reader, ContentLibrary content)` with `PreviewAsync(string repoPath, ActionRequest request, CancellationToken ct = default)` and `RunAsync(string repoPath, ActionRequest request, CancellationToken ct = default)`

**Two design rules from the spec, both enforced by tests:**

1. **Preview runs nothing.** `PreviewAsync` only reads state and builds argv. A test asserts that previewing a commit leaves the history untouched.
2. **`RunAsync` re-validates preconditions itself.** The caller is not trusted, and state may have changed between preview and run. A blocked run returns `Success: false` with `Blockers` populated and **no git command executed**.

**Narration describes the observed difference, never the intended one.** `Narrator` receives only the before and after snapshots — it has no idea which action ran. That is deliberate: it makes it structurally impossible for the app to report a success that did not happen.

- [ ] **Step 1: Write the failing tests**

Create `tests/GitHelper.Core.Tests/NarratorTests.cs`:

```csharp
using GitHelper.Core.Actions;
using GitHelper.Core.Model;

namespace GitHelper.Core.Tests;

public class NarratorTests
{
    private static RepoState State(
        string? branch = "main",
        int ahead = 0,
        int behind = 0,
        CommitInfo[]? commits = null,
        params FileChange[] changes)
        => new(
            @"C:\repos\demo", branch, branch is null, "origin/main", ahead, behind,
            HasCommits: commits is { Length: > 0 },
            HasRemote: true,
            Changes: changes,
            RecentCommits: commits ?? Array.Empty<CommitInfo>(),
            Branches: Array.Empty<BranchInfo>());

    private static CommitInfo Commit(string hash, string subject)
        => new(hash + "0000", hash, "Test User", DateTimeOffset.UnixEpoch, subject);

    [Fact]
    public void Describe_ReportsANewCommitWithItsShortHash()
    {
        var before = State(commits: new[] { Commit("aaa", "initial") });
        var after = State(commits: new[] { Commit("bbb", "second"), Commit("aaa", "initial") });

        var narration = Narrator.Describe(before, after);

        Assert.Contains("bbb", narration);
        Assert.Contains("second", narration);
    }

    [Fact]
    public void Describe_ReportsARemovedCommit()
    {
        var before = State(commits: new[] { Commit("bbb", "second"), Commit("aaa", "initial") });
        var after = State(commits: new[] { Commit("aaa", "initial") });

        var narration = Narrator.Describe(before, after);

        Assert.Contains("removed", narration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Describe_ReportsABranchChange()
    {
        var narration = Narrator.Describe(State(branch: "main"), State(branch: "feature"));

        Assert.Contains("feature", narration);
    }

    [Fact]
    public void Describe_ReportsStagingChanges()
    {
        var before = State(changes: new FileChange("a.txt", null, ChangeKind.None, ChangeKind.Modified));
        var after = State(changes: new FileChange("a.txt", null, ChangeKind.Modified, ChangeKind.None));

        var narration = Narrator.Describe(before, after);

        Assert.Contains("staged", narration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Describe_ReportsAheadAndBehindMovement()
    {
        var narration = Narrator.Describe(State(ahead: 2), State(ahead: 0));

        Assert.Contains("origin/main", narration);
    }

    [Fact]
    public void Describe_SaysSoWhenNothingObservablyChanged()
    {
        var narration = Narrator.Describe(State(), State());

        Assert.Contains("no change", narration, StringComparison.OrdinalIgnoreCase);
    }
}
```

Create `tests/GitHelper.Core.Tests/ActionServiceTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter "NarratorTests|ActionServiceTests"`
Expected: FAIL — `Narrator`, `ActionService`, `ActionPreview`, `ActionOutcome` do not exist (CS0246).

- [ ] **Step 3: Write Narrator**

Create `src/GitHelper.Core/Actions/Narrator.cs`:

```csharp
using GitHelper.Core.Model;

namespace GitHelper.Core.Actions;

/// <summary>
/// Describes what actually changed between two snapshots.
///
/// This deliberately does not know which action ran. Narrating the observed difference
/// rather than the intended one makes it structurally impossible for the app to report a
/// success that did not happen.
/// </summary>
public static class Narrator
{
    public static string Describe(RepoState before, RepoState after)
    {
        var parts = new List<string>();

        DescribeCommits(before, after, parts);
        DescribeBranch(before, after, parts);
        DescribeStaging(before, after, parts);
        DescribeSync(before, after, parts);

        return parts.Count == 0
            ? "No change that this app can see."
            : string.Join(" ", parts);
    }

    private static void DescribeCommits(RepoState before, RepoState after, List<string> parts)
    {
        var beforeHashes = before.RecentCommits.Select(c => c.Hash).ToHashSet(StringComparer.Ordinal);
        var afterHashes = after.RecentCommits.Select(c => c.Hash).ToHashSet(StringComparer.Ordinal);

        var added = after.RecentCommits.Where(c => !beforeHashes.Contains(c.Hash)).ToList();
        var removed = before.RecentCommits.Where(c => !afterHashes.Contains(c.Hash)).ToList();

        foreach (var commit in added)
            parts.Add($"Created commit {commit.ShortHash} \"{commit.Subject}\".");

        foreach (var commit in removed)
            parts.Add($"Removed commit {commit.ShortHash} \"{commit.Subject}\" from the history.");
    }

    private static void DescribeBranch(RepoState before, RepoState after, List<string> parts)
    {
        if (string.Equals(before.Branch, after.Branch, StringComparison.Ordinal)) return;

        parts.Add(after.Branch is null
            ? "You are no longer on a branch."
            : $"You are now on branch {after.Branch}.");
    }

    private static void DescribeStaging(RepoState before, RepoState after, List<string> parts)
    {
        var stagedDelta = after.Staged.Count - before.Staged.Count;

        if (stagedDelta > 0)
            parts.Add($"Staged {stagedDelta} file(s).");
        else if (stagedDelta < 0 && after.RecentCommits.Count == before.RecentCommits.Count)
            // A drop in staged files after a commit is already covered by the commit sentence.
            parts.Add($"Unstaged {-stagedDelta} file(s).");
    }

    private static void DescribeSync(RepoState before, RepoState after, List<string> parts)
    {
        if (after.Upstream is null) return;
        if (before.Ahead == after.Ahead && before.Behind == after.Behind) return;

        var position = (after.Ahead, after.Behind) switch
        {
            (0, 0) => $"in step with {after.Upstream}",
            (> 0, 0) => $"{after.Ahead} commit(s) ahead of {after.Upstream}",
            (0, > 0) => $"{after.Behind} commit(s) behind {after.Upstream}",
            var (a, b) => $"{a} ahead of and {b} behind {after.Upstream}",
        };

        parts.Add($"Your branch is now {position}.");
    }
}
```

- [ ] **Step 4: Write ActionPreview and ActionOutcome**

Create `src/GitHelper.Core/Actions/ActionPreview.cs`:

```csharp
using GitHelper.Core.Content;

namespace GitHelper.Core.Actions;

/// <summary>
/// Everything the explain panel needs, produced without running anything.
/// </summary>
public sealed record ActionPreview(
    GitAction Action,
    IReadOnlyList<string> ArgVector,
    string CommandLine,
    ExplanationDocument Explanation,
    IReadOnlyDictionary<string, string> Slots,
    IReadOnlyList<PreconditionResult> Blockers,
    Danger Danger,
    string? UndoActionId)
{
    public bool CanRun => Blockers.Count == 0;
}
```

Create `src/GitHelper.Core/Actions/ActionOutcome.cs`:

```csharp
using GitHelper.Core.Errors;
using GitHelper.Core.Git;
using GitHelper.Core.Model;

namespace GitHelper.Core.Actions;

/// <summary>The result of running an action, including what observably changed.</summary>
public sealed record ActionOutcome(
    bool Success,
    GitCommandResult Result,
    string? Narration,
    TranslatedError? Error,
    RepoState Before,
    RepoState After,
    IReadOnlyList<PreconditionResult> Blockers);
```

- [ ] **Step 5: Write ActionService**

Create `src/GitHelper.Core/Actions/ActionService.cs`:

```csharp
using GitHelper.Core.Content;
using GitHelper.Core.Errors;
using GitHelper.Core.Git;
using GitHelper.Core.Repo;

namespace GitHelper.Core.Actions;

/// <summary>The preview-then-run flow that every action goes through.</summary>
public sealed class ActionService(
    IGitRunner runner,
    RepoStateReader reader,
    ContentLibrary content)
{
    /// <summary>
    /// Builds everything the explain panel needs. Runs no git command that changes anything —
    /// only the read-only queries needed to describe what would happen.
    /// </summary>
    public async Task<ActionPreview> PreviewAsync(
        string repoPath,
        ActionRequest request,
        CancellationToken ct = default)
    {
        var action = Resolve(request.ActionId);
        var state = await reader.ReadAsync(repoPath, ct);

        var blockers = Evaluate(action, state, request);
        var slots = SlotBinder.Bind(state, request.Path, request.BranchName);

        // argv is only built when it can be built; a missing path would throw otherwise.
        var args = blockers.Count == 0
            ? action.BuildArgs(state, request)
            : Array.Empty<string>();

        var commandLine = args.Count == 0 ? string.Empty : "git " + string.Join(' ', args);

        return new ActionPreview(
            Action: action,
            ArgVector: args,
            CommandLine: commandLine,
            Explanation: content.Actions[action.ExplanationId],
            Slots: slots,
            Blockers: blockers,
            Danger: action.Danger,
            UndoActionId: action.UndoActionId);
    }

    /// <summary>
    /// Runs the action. Preconditions are re-evaluated here rather than trusted from the
    /// preview: the caller is not trusted, and state may have changed since.
    /// </summary>
    public async Task<ActionOutcome> RunAsync(
        string repoPath,
        ActionRequest request,
        CancellationToken ct = default)
    {
        var action = Resolve(request.ActionId);
        var before = await reader.ReadAsync(repoPath, ct);

        var blockers = Evaluate(action, before, request);
        if (blockers.Count > 0)
        {
            return new ActionOutcome(
                Success: false,
                Result: new GitCommandResult(Array.Empty<string>(), "", "", 0, TimeSpan.Zero),
                Narration: null,
                Error: null,
                Before: before,
                After: before,
                Blockers: blockers);
        }

        var args = action.BuildArgs(before, request);
        var result = await runner.RunAsync(repoPath, args, ct);
        var after = await reader.ReadAsync(repoPath, ct);

        return new ActionOutcome(
            Success: result.Success,
            Result: result,
            Narration: result.Success ? Narrator.Describe(before, after) : null,
            Error: ErrorTranslator.Translate(result),
            Before: before,
            After: after,
            Blockers: Array.Empty<PreconditionResult>());
    }

    private static GitAction Resolve(string actionId)
        => ActionCatalog.Find(actionId)
           ?? throw new ArgumentException($"Unknown action id '{actionId}'.", nameof(actionId));

    private static IReadOnlyList<PreconditionResult> Evaluate(
        GitAction action, Model.RepoState state, ActionRequest request)
        => action.Preconditions
            .Select(p => p.Evaluate(state, request))
            .Where(r => !r.Satisfied)
            .ToList();
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test --filter "NarratorTests|ActionServiceTests"`
Expected: PASS, 14 tests.

Note on `RunAsync_TranslatesAFailureFromGit`: it points a remote at an unreachable host, so the failure comes from git rather than a precondition. Expect it to take a few seconds while git gives up on the connection.

- [ ] **Step 7: Run the whole suite**

Run: `dotnet test`
Expected: PASS, everything from Tasks 1–12.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: add preview and run flow with observed-change narration"
```

---

### Task 13: Startup environment checks

**Files:**
- Create: `src/GitHelper.Core/Git/EnvironmentCheck.cs`
- Create: `src/GitHelper.Core/Git/GitEnvironment.cs`
- Test: `tests/GitHelper.Core.Tests/GitEnvironmentTests.cs`

**Interfaces:**
- Consumes: `IGitRunner`, `GitCommandResult` (Task 2).
- Produces:
  - `enum CheckStatus { Ok, Warning, Blocking }`
  - `EnvironmentCheck(string Id, CheckStatus Status, string Summary, string Explanation, string? FixHint)`
  - `GitEnvironment(IGitRunner runner)` with:
    - `CheckAsync(CancellationToken ct = default) -> Task<IReadOnlyList<EnvironmentCheck>>`
    - `SetIdentityAsync(string name, string email, CancellationToken ct = default) -> Task<GitCommandResult>`
  - `GitEnvironment.IsUsable(IReadOnlyList<EnvironmentCheck> checks) -> bool`

**Why identity is checked at startup rather than at commit time.** An unconfigured `user.name` / `user.email` makes a beginner's *very first commit* fail with a wall of git configuration advice — the worst possible first experience. Detecting it before that moment, and offering to set it, is the entire point.

`git` not being on PATH is `Blocking`: nothing in the app can work. A missing identity is a `Warning`: the user can browse and stage, and only committing is affected.

**A note on the identity check.** `git config user.name` exits non-zero when the key is unset, which is the signal used here rather than an empty string. The check runs with the process working directory, so it reflects global configuration; a repository-local override is picked up too when the app is started inside one.

- [ ] **Step 1: Write the failing test**

Create `tests/GitHelper.Core.Tests/GitEnvironmentTests.cs`:

```csharp
using GitHelper.Core.Git;

namespace GitHelper.Core.Tests;

public class GitEnvironmentTests
{
    /// <summary>A runner returning canned results, so the checks can be tested without a machine state.</summary>
    private sealed class StubRunner(Func<IReadOnlyList<string>, GitCommandResult> respond) : IGitRunner
    {
        public Task<GitCommandResult> RunAsync(
            string workingDirectory, IReadOnlyList<string> args, CancellationToken ct = default)
            => Task.FromResult(respond(args));
    }

    private static GitCommandResult Ok(IReadOnlyList<string> args, string stdOut)
        => new(args, stdOut, "", 0, TimeSpan.Zero);

    private static GitCommandResult Fail(IReadOnlyList<string> args, string stdErr)
        => new(args, "", stdErr, 1, TimeSpan.Zero);

    [Fact]
    public async Task CheckAsync_ReportsEverythingHealthy()
    {
        var runner = new StubRunner(args => args[0] switch
        {
            "--version" => Ok(args, "git version 2.55.0.windows.3"),
            "config" when args[1] == "user.name" => Ok(args, "Ada Lovelace"),
            "config" => Ok(args, "ada@example.com"),
            _ => Fail(args, "unexpected"),
        });

        var checks = await new GitEnvironment(runner).CheckAsync();

        Assert.All(checks, c => Assert.Equal(CheckStatus.Ok, c.Status));
        Assert.True(GitEnvironment.IsUsable(checks));
        Assert.Contains(checks, c => c.Id == "git-version" && c.Summary.Contains("2.55"));
    }

    [Fact]
    public async Task CheckAsync_BlocksWhenGitIsNotInstalled()
    {
        var runner = new StubRunner(_ => throw new System.ComponentModel.Win32Exception("not found"));

        var checks = await new GitEnvironment(runner).CheckAsync();

        var gitCheck = Assert.Single(checks, c => c.Id == "git-present");
        Assert.Equal(CheckStatus.Blocking, gitCheck.Status);
        Assert.False(GitEnvironment.IsUsable(checks));
        Assert.NotNull(gitCheck.FixHint);
    }

    [Fact]
    public async Task CheckAsync_WarnsButDoesNotBlockWhenIdentityIsMissing()
    {
        var runner = new StubRunner(args => args[0] switch
        {
            "--version" => Ok(args, "git version 2.55.0"),
            // git config exits non-zero when the key is unset.
            "config" => Fail(args, ""),
            _ => Fail(args, "unexpected"),
        });

        var checks = await new GitEnvironment(runner).CheckAsync();

        var identity = Assert.Single(checks, c => c.Id == "git-identity");
        Assert.Equal(CheckStatus.Warning, identity.Status);
        Assert.Contains("commit", identity.Explanation, StringComparison.OrdinalIgnoreCase);
        // A missing identity still lets the user browse and stage.
        Assert.True(GitEnvironment.IsUsable(checks));
    }

    [Fact]
    public async Task CheckAsync_WarnsWhenOnlyTheEmailIsMissing()
    {
        var runner = new StubRunner(args => args[0] switch
        {
            "--version" => Ok(args, "git version 2.55.0"),
            "config" when args[1] == "user.name" => Ok(args, "Ada Lovelace"),
            "config" => Fail(args, ""),
            _ => Fail(args, "unexpected"),
        });

        var checks = await new GitEnvironment(runner).CheckAsync();

        Assert.Equal(CheckStatus.Warning, Assert.Single(checks, c => c.Id == "git-identity").Status);
    }

    [Fact]
    public async Task SetIdentityAsync_WritesBothValuesGlobally()
    {
        var calls = new List<IReadOnlyList<string>>();
        var runner = new StubRunner(args =>
        {
            calls.Add(args);
            return Ok(args, "");
        });

        await new GitEnvironment(runner).SetIdentityAsync("Ada Lovelace", "ada@example.com");

        Assert.Equal(2, calls.Count);
        Assert.All(calls, c => Assert.Contains("--global", c));
        Assert.Contains(calls, c => c.Contains("user.name") && c.Contains("Ada Lovelace"));
        Assert.Contains(calls, c => c.Contains("user.email") && c.Contains("ada@example.com"));
    }

    [Fact]
    public async Task CheckAsync_RunsAgainstTheRealGitOnThisMachine()
    {
        var checks = await new GitEnvironment(new GitRunner()).CheckAsync();

        // git is a prerequisite for this project, so it must be present here.
        Assert.Equal(CheckStatus.Ok, Assert.Single(checks, c => c.Id == "git-present").Status);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter GitEnvironmentTests`
Expected: FAIL — `GitEnvironment`, `EnvironmentCheck`, `CheckStatus` do not exist (CS0246).

- [ ] **Step 3: Write EnvironmentCheck**

Create `src/GitHelper.Core/Git/EnvironmentCheck.cs`:

```csharp
namespace GitHelper.Core.Git;

public enum CheckStatus
{
    Ok,

    /// <summary>Something will go wrong later, but the app is still usable now.</summary>
    Warning,

    /// <summary>The app cannot function at all.</summary>
    Blocking,
}

/// <summary>One startup check, phrased for someone who does not know git.</summary>
public sealed record EnvironmentCheck(
    string Id,
    CheckStatus Status,
    string Summary,
    string Explanation,
    string? FixHint);
```

- [ ] **Step 4: Write GitEnvironment**

Create `src/GitHelper.Core/Git/GitEnvironment.cs`:

```csharp
using System.ComponentModel;

namespace GitHelper.Core.Git;

/// <summary>Checks the machine can actually do what the app is about to offer.</summary>
public sealed class GitEnvironment(IGitRunner runner)
{
    public async Task<IReadOnlyList<EnvironmentCheck>> CheckAsync(CancellationToken ct = default)
    {
        var checks = new List<EnvironmentCheck>();
        var workingDirectory = Directory.GetCurrentDirectory();

        GitCommandResult? version = null;
        try
        {
            version = await runner.RunAsync(workingDirectory, new[] { "--version" }, ct);
        }
        catch (Win32Exception)
        {
            // The executable itself could not be started: git is not on PATH.
        }

        if (version is null || !version.Success)
        {
            checks.Add(new EnvironmentCheck(
                Id: "git-present",
                Status: CheckStatus.Blocking,
                Summary: "Git is not installed",
                Explanation:
                    "This app is a friendly front end for git, so git itself has to be on your "
                    + "computer. It is a free download and takes a couple of minutes to install.",
                FixHint: "Install Git for Windows from https://git-scm.com/download/win, then restart this app."));

            return checks;
        }

        checks.Add(new EnvironmentCheck(
            Id: "git-present",
            Status: CheckStatus.Ok,
            Summary: "Git is installed",
            Explanation: "Git was found on your computer.",
            FixHint: null));

        checks.Add(new EnvironmentCheck(
            Id: "git-version",
            Status: CheckStatus.Ok,
            Summary: version.StdOut.Trim(),
            Explanation: "The version of git this app is driving.",
            FixHint: null));

        checks.Add(await CheckIdentityAsync(workingDirectory, ct));
        return checks;
    }

    private async Task<EnvironmentCheck> CheckIdentityAsync(string workingDirectory, CancellationToken ct)
    {
        // git config exits non-zero when a key is unset, which is the signal used here.
        var name = await runner.RunAsync(workingDirectory, new[] { "config", "user.name" }, ct);
        var email = await runner.RunAsync(workingDirectory, new[] { "config", "user.email" }, ct);

        var hasName = name.Success && name.StdOut.Trim().Length > 0;
        var hasEmail = email.Success && email.StdOut.Trim().Length > 0;

        if (hasName && hasEmail)
        {
            return new EnvironmentCheck(
                Id: "git-identity",
                Status: CheckStatus.Ok,
                Summary: $"Signing commits as {name.StdOut.Trim()}",
                Explanation: "Your name and email are set, so commits will be labelled with them.",
                FixHint: null);
        }

        return new EnvironmentCheck(
            Id: "git-identity",
            Status: CheckStatus.Warning,
            Summary: "Your name and email are not set yet",
            Explanation:
                "Every commit records who made it. Until this is set, your very first commit will "
                + "fail with a confusing message, so it is worth doing now. This is only a label — "
                + "it is not an account, and no password is involved.",
            FixHint: "Enter a name and email, and this app will save them for git.");
    }

    /// <summary>Writes the identity to the user's global git configuration.</summary>
    public async Task<GitCommandResult> SetIdentityAsync(
        string name, string email, CancellationToken ct = default)
    {
        var workingDirectory = Directory.GetCurrentDirectory();

        await runner.RunAsync(workingDirectory, new[] { "config", "--global", "user.name", name }, ct);
        return await runner.RunAsync(
            workingDirectory, new[] { "config", "--global", "user.email", email }, ct);
    }

    public static bool IsUsable(IReadOnlyList<EnvironmentCheck> checks)
        => checks.All(c => c.Status != CheckStatus.Blocking);
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test --filter GitEnvironmentTests`
Expected: PASS, 6 tests.

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test`
Expected: PASS, everything from Tasks 1–13.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: add startup environment checks with identity setup"
```

---

### Task 14: The command log

**Files:**
- Create: `src/GitHelper.Core/Git/CommandLogEntry.cs`
- Create: `src/GitHelper.Core/Git/CommandLog.cs`
- Create: `src/GitHelper.Core/Git/LoggingGitRunner.cs`
- Test: `tests/GitHelper.Core.Tests/CommandLogTests.cs`

**Interfaces:**
- Consumes: `IGitRunner`, `GitCommandResult` (Task 2).
- Produces:
  - `CommandLogEntry(DateTimeOffset At, string CommandLine, int ExitCode, TimeSpan Duration, bool Success)`
  - `CommandLog` with `Entries -> IReadOnlyList<CommandLogEntry>`, `Record(GitCommandResult result)`, `Clear()`, `ToClipboardText() -> string`, and an `event EventHandler<CommandLogEntry>? EntryRecorded`
  - `LoggingGitRunner(IGitRunner inner, CommandLog log) : IGitRunner`

**Why a decorator rather than logging inside `GitRunner`.** `GitRunner`'s single responsibility is starting a process correctly, and it is the hardest part of the system to get right. Wrapping it keeps that class untouched while still capturing every invocation, because the decorator sits on the one interface everything else depends on. The UI composes `new LoggingGitRunner(new GitRunner(), log)` once at startup and nothing else changes.

**Read-only queries are logged too.** The spec's aim is that a user absorbs the CLI by watching it accumulate, and `git status` is the command they will most need to recognise. The UI can filter if the noise becomes a problem; the log itself does not decide.

`CommandLog` is thread-safe because state refreshes run off the UI thread while actions are also running.

- [ ] **Step 1: Write the failing test**

Create `tests/GitHelper.Core.Tests/CommandLogTests.cs`:

```csharp
using GitHelper.Core.Git;

namespace GitHelper.Core.Tests;

public class CommandLogTests
{
    private sealed class StubRunner(GitCommandResult result) : IGitRunner
    {
        public int Calls { get; private set; }

        public Task<GitCommandResult> RunAsync(
            string workingDirectory, IReadOnlyList<string> args, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private static GitCommandResult Result(int exitCode = 0)
        => new(new[] { "status" }, "", "", exitCode, TimeSpan.FromMilliseconds(12));

    [Fact]
    public void Record_KeepsCommandsInTheOrderTheyRan()
    {
        var log = new CommandLog();

        log.Record(new GitCommandResult(new[] { "status" }, "", "", 0, TimeSpan.Zero));
        log.Record(new GitCommandResult(new[] { "add", "-A" }, "", "", 0, TimeSpan.Zero));

        Assert.Equal(new[] { "git status", "git add -A" }, log.Entries.Select(e => e.CommandLine));
    }

    [Fact]
    public void Record_CapturesFailureAsWellAsSuccess()
    {
        var log = new CommandLog();

        log.Record(Result(exitCode: 1));

        var entry = Assert.Single(log.Entries);
        Assert.False(entry.Success);
        Assert.Equal(1, entry.ExitCode);
    }

    [Fact]
    public void Record_RaisesAnEventSoTheUiCanAppendWithoutPolling()
    {
        var log = new CommandLog();
        CommandLogEntry? received = null;
        log.EntryRecorded += (_, entry) => received = entry;

        log.Record(Result());

        Assert.NotNull(received);
        Assert.Equal("git status", received!.CommandLine);
    }

    [Fact]
    public void ToClipboardText_ProducesCommandsAUserCouldPasteIntoATerminal()
    {
        var log = new CommandLog();
        log.Record(new GitCommandResult(new[] { "status" }, "", "", 0, TimeSpan.Zero));
        log.Record(new GitCommandResult(new[] { "add", "-A" }, "", "", 0, TimeSpan.Zero));

        var text = log.ToClipboardText();

        Assert.Equal("git status\ngit add -A", text.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Clear_EmptiesTheLog()
    {
        var log = new CommandLog();
        log.Record(Result());

        log.Clear();

        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task LoggingGitRunner_RecordsEveryInvocationAndReturnsTheInnerResult()
    {
        var inner = new StubRunner(Result());
        var log = new CommandLog();
        var runner = new LoggingGitRunner(inner, log);

        var result = await runner.RunAsync(@"C:\repo", new[] { "status" });

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, inner.Calls);
        Assert.Single(log.Entries);
    }

    [Fact]
    public async Task LoggingGitRunner_CapturesReadOnlyQueriesToo()
    {
        // The user learns the CLI by watching it accumulate, and status is the command
        // they will most need to recognise.
        var log = new CommandLog();
        var runner = new LoggingGitRunner(new GitRunner(), log);
        using var repo = await TestRepo.CreateAsync();

        await runner.RunAsync(repo.Path, new[] { "status", "--porcelain=v2", "-z", "--branch" });

        Assert.Contains(log.Entries, e => e.CommandLine.StartsWith("git status"));
    }

    [Fact]
    public void Record_IsSafeToCallFromSeveralThreadsAtOnce()
    {
        var log = new CommandLog();

        Parallel.For(0, 500, _ => log.Record(Result()));

        Assert.Equal(500, log.Entries.Count);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter CommandLogTests`
Expected: FAIL — `CommandLog`, `CommandLogEntry`, `LoggingGitRunner` do not exist (CS0246).

- [ ] **Step 3: Write CommandLogEntry**

Create `src/GitHelper.Core/Git/CommandLogEntry.cs`:

```csharp
namespace GitHelper.Core.Git;

/// <summary>One git command as it appeared to the user.</summary>
public sealed record CommandLogEntry(
    DateTimeOffset At,
    string CommandLine,
    int ExitCode,
    TimeSpan Duration,
    bool Success);
```

- [ ] **Step 4: Write CommandLog**

Create `src/GitHelper.Core/Git/CommandLog.cs`:

```csharp
namespace GitHelper.Core.Git;

/// <summary>
/// Every git command run this session. This is the mechanism by which a user outgrows the
/// app: the CLI is absorbed by watching it accumulate.
///
/// Thread-safe, because state refreshes run off the UI thread while actions may also be running.
/// </summary>
public sealed class CommandLog
{
    private readonly List<CommandLogEntry> _entries = new();
    private readonly Lock _gate = new();

    public event EventHandler<CommandLogEntry>? EntryRecorded;

    public IReadOnlyList<CommandLogEntry> Entries
    {
        get
        {
            lock (_gate) return _entries.ToArray();
        }
    }

    public void Record(GitCommandResult result)
    {
        var entry = new CommandLogEntry(
            At: DateTimeOffset.Now,
            CommandLine: result.CommandLine,
            ExitCode: result.ExitCode,
            Duration: result.Duration,
            Success: result.Success);

        lock (_gate) _entries.Add(entry);

        // Raised outside the lock so a handler cannot deadlock the runner.
        EntryRecorded?.Invoke(this, entry);
    }

    public void Clear()
    {
        lock (_gate) _entries.Clear();
    }

    /// <summary>The commands alone, ready to paste into a terminal.</summary>
    public string ToClipboardText()
        => string.Join(Environment.NewLine, Entries.Select(e => e.CommandLine));
}
```

- [ ] **Step 5: Write LoggingGitRunner**

Create `src/GitHelper.Core/Git/LoggingGitRunner.cs`:

```csharp
namespace GitHelper.Core.Git;

/// <summary>
/// Records every invocation, then delegates. A decorator rather than logging inside
/// GitRunner: that class has one job, starting a process correctly, and it is the part of
/// the system least worth disturbing.
/// </summary>
public sealed class LoggingGitRunner(IGitRunner inner, CommandLog log) : IGitRunner
{
    public async Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> args,
        CancellationToken ct = default)
    {
        var result = await inner.RunAsync(workingDirectory, args, ct);
        log.Record(result);
        return result;
    }
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test --filter CommandLogTests`
Expected: PASS, 8 tests.

If `Lock` is not recognised, the project is not on .NET 9 or later; confirm `net10.0` in the `.csproj`. Substituting `private readonly object _gate = new();` also works.

- [ ] **Step 7: Run the whole suite**

Run: `dotnet test`
Expected: PASS. This is the complete engine.

- [ ] **Step 8: Confirm Core still has no UI dependency**

This is the constraint the whole architecture rests on, so verify it rather than assume it:

```bash
dotnet list src/GitHelper.Core/GitHelper.Core.csproj package
```

Expected: only `YamlDotNet`. No Avalonia, no MVVM, no UI package.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: record every git command in a session command log"
```

---

## Done

At this point the engine is complete and fully tested with no UI:

- Real git invoked through one argv-only choke point, with every invocation recorded.
- Repository state parsed into one immutable snapshot, covering empty repos, detached HEAD, renames, and non-ASCII paths.
- Thirteen actions expressed as data, each with preconditions, a danger level, and authored content.
- Content and code held together by integrity tests, so an explanation cannot silently go missing or drift out of date.
- Git failures translated into plain English, with the raw output always reachable and unknown errors admitted rather than guessed.
- Preview that runs nothing, and a run path that re-validates and narrates the observed change.

## Deferred to Plan 2, deliberately

Recorded here so they are not lost:

- The Avalonia application, and the Avalonia version choice, which nothing in Plan 1 constrains.
- The explanation renderer for the block schema, including glossary underline-and-hover.
- Confirmation gates by danger level, and the per-action "stop explaining this one" preference with its settings file.
- The command log **pane** — the data behind it is Task 14.
- The debounced `FileSystemWatcher` that refreshes `RepoState`.
- Packaging to a single self-contained `.exe`.
