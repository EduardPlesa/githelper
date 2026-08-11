# Tags and Stash (v1.1) Implementation Plan

> ## ⚠ Shipped. This is a record, not instructions.
>
> **Do not execute this plan.** Tags and stash are implemented and on `main`. The tasks below
> describe a codebase that no longer exists: they were written against a `RepoState` with no
> `Operation` field, before v2 shipped operation state and merge, and every exact-match anchor
> in them is stale. An agent following them would fail on the first edit, or worse, half-succeed.
>
> Kept because the reasoning is worth reading — particularly why `stash-pop` and `stash-apply`
> are gated on a clean working tree, and why `stash-drop` is the app's second `Destructive`
> action. For what the code does today, read the code.
>
> **Where the shipped work diverged from this plan:**
>
> - `RepoState`'s new fields landed as `… Branches, Tags, Stashes, Operation`. This plan
>   predates `Operation` entirely.
> - `stash` carries `RequiresNoOperationInProgress`, and `ChangesViewModel.CanStash` is gated on
>   `state.Operation is null`. Neither is in this plan — both only became necessary once merge
>   existed, because a conflicted merge counts as uncommitted changes.
> - `RequiresStashRef` also checks the ref still exists, rather than only that one was picked.
> - `stash-pop` / `stash-apply` roll the repository back when they conflict with commits made
>   since the stash was set aside. This plan assumed a clean working tree ruled that out; it
>   does not — a clean tree says nothing about the commits underneath.
> - `create-tag` / `delete-tag` pass `--` before the tag name, separating it from any flags.
> - `stash-drop` being the second `Destructive` action forced the destructive modal's
>   consequence sentence to stop being hardcoded for `discard-file`.
> - The app has **25 actions, 15 glossary terms** today.
>
> One further note, because it is the most useful thing here: bringing this work together with
> v2 was botched in `6d7c67f`, which resolved conflicts file-by-file and left `main` unable to
> compile. That was repaired in #19. Both features were complete; the merge halved each. If you
> are about to merge two branches that touched the same positional record, build in between.

**Superseded — the agentic-worker instruction that was here has been removed deliberately, so that
no subagent picks this file up and tries to execute it.**

**Goal:** Close out GitHelper v1.1 by adding tags (create/delete) and stash (set aside/bring back/copy back/delete) as ordinary `GitAction`s, matching the design in `docs/superpowers/specs/2026-08-03-tags-and-stash-design.md`.

**Architecture:** Both features are `GitAction` descriptors plus content files — no new operation kind, no change to the preview/run/narrate flow. Each needs new read state (`RepoState.Tags` / `RepoState.Stashes`), a parser, new preconditions, and a UI section embedded in an existing tab (Tags in Branches, Stash in Changes) rather than a new `MainTab`.

**Tech Stack:** C# / .NET 10, Avalonia (MVVM via CommunityToolkit.Mvvm), xUnit, YamlDotNet for content frontmatter. No new dependencies.

## Global Constraints

- `TreatWarningsAsErrors` is `true` in every project — an unused `using` fails the build, not just a lint.
- Every `GitRunner` invocation is an argv array, never a constructed string. Never introduce stdin or shell-string building.
- `git tag -d`, unlike `git branch -d`, has no refusal safety net — `delete-tag` is `Danger.Caution`, not `Safe`.
- `stash-pop` / `stash-apply` must only ever run against a clean working tree (`RequiresNoUncommittedChanges`), so a conflicting pop can never happen through this UI — the app has no operation-state model yet (roadmap Bucket 2, not this plan).
- `stash-drop` is `Danger.Destructive` (unrecoverable), matching `discard-file`.
- Content file id must equal the action id (`GitAction.ExplanationId => Id`). Every action needs `## what`, `## risks`, `## undo` sections; `ContentIntegrityTests` enforces this and will fail loudly if a file is missing or a frontmatter field drifts from the descriptor.
- `.md` content files are picked up automatically by wildcard (`GitHelper.Content.csproj`); no manual registration.
- Run tests with `dotnet test GitHelper.sln` from the repo root. Use `--filter "FullyQualifiedName~ClassName"` for a fast targeted run while iterating, and a full `dotnet test` before each commit.

---

### Task 1: Extend the domain model with `Tags` and `Stashes`

`RepoState` gains two new fields. Because it's a positional record, every existing construction site (production and test) must be updated in the same task or the solution will not compile — there is no way to give the new fields a shared default in a positional record, and updating every call site is the cost the roadmap already budgeted for this bucket.

**Files:**
- Create: `src/GitHelper.Core/Model/TagInfo.cs`
- Create: `src/GitHelper.Core/Model/StashInfo.cs`
- Modify: `src/GitHelper.Core/Model/RepoState.cs`
- Modify: `src/GitHelper.Core/Repo/RepoStateReader.cs` (placeholder empty lists here; wired for real in Task 4)
- Modify: `tests/GitHelper.Core.Tests/ActionCatalogTests.cs`
- Modify: `tests/GitHelper.Core.Tests/SlotBinderTests.cs`
- Modify: `tests/GitHelper.Core.Tests/NarratorTests.cs`
- Modify: `tests/GitHelper.Core.Tests/PreconditionTests.cs`
- Modify: `tests/GitHelper.App.Tests/BranchesViewModelTests.cs`
- Modify: `tests/GitHelper.App.Tests/TabViewTests.cs`
- Modify: `tests/GitHelper.App.Tests/ChangesGitignoreBannerTests.cs`
- Modify: `tests/GitHelper.App.Tests/ChangesPushPromptTests.cs`
- Modify: `tests/GitHelper.App.Tests/ChangesConnectRemoteTests.cs`
- Modify: `tests/GitHelper.App.Tests/ChangesViewModelTests.cs`

**Interfaces:**
- Produces: `TagInfo(string Name, string Target)`, `StashInfo(string Ref, string Message)`, `RepoState.Tags` (`IReadOnlyList<TagInfo>`), `RepoState.Stashes` (`IReadOnlyList<StashInfo>`) — every later task reads these exact names and types.

- [ ] **Step 1: Create the two new model records**

`src/GitHelper.Core/Model/TagInfo.cs`:

```csharp
namespace GitHelper.Core.Model;

/// <summary>
/// A tag: a fixed name pointing at one commit. <paramref name="Target"/> is the short hash
/// it points to, for display only.
/// </summary>
public sealed record TagInfo(string Name, string Target);
```

`src/GitHelper.Core/Model/StashInfo.cs`:

```csharp
namespace GitHelper.Core.Model;

/// <summary>
/// One stash entry. <paramref name="Ref"/> is git's own selector (e.g. "stash@{0}") and is
/// what every stash action passes straight back to git — it is never re-derived from the
/// entry's position in the list.
/// </summary>
public sealed record StashInfo(string Ref, string Message);
```

- [ ] **Step 2: Add the two fields to `RepoState`**

In `src/GitHelper.Core/Model/RepoState.cs`, change:

```csharp
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
```

to:

```csharp
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
    IReadOnlyList<BranchInfo> Branches,
    IReadOnlyList<TagInfo> Tags,
    IReadOnlyList<StashInfo> Stashes)
{
```

- [ ] **Step 3: Fix the one production call site**

In `src/GitHelper.Core/Repo/RepoStateReader.cs`, change:

```csharp
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
```

to:

```csharp
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
            Branches: branches,
            Tags: Array.Empty<TagInfo>(),
            Stashes: Array.Empty<StashInfo>());
```

(Task 4 replaces the two `Array.Empty<...>()` placeholders with real reads.)

- [ ] **Step 4: Fix the seven simple test call sites**

Each of the files below has exactly one line reading `Branches: Array.Empty<BranchInfo>());` (confirmed via a repo-wide search before writing this plan — each occurrence is unique within its file). In each file, change that one line to add the two new fields, keeping that file's existing indentation:

`tests/GitHelper.Core.Tests/SlotBinderTests.cs` and `tests/GitHelper.Core.Tests/NarratorTests.cs` and `tests/GitHelper.App.Tests/ChangesPushPromptTests.cs` and `tests/GitHelper.App.Tests/ChangesViewModelTests.cs` (12-space indent):

```csharp
            Branches: Array.Empty<BranchInfo>());
```

becomes:

```csharp
            Branches: Array.Empty<BranchInfo>(),
            Tags: Array.Empty<TagInfo>(),
            Stashes: Array.Empty<StashInfo>());
```

`tests/GitHelper.App.Tests/ChangesGitignoreBannerTests.cs` and `tests/GitHelper.App.Tests/ChangesConnectRemoteTests.cs` (8-space indent):

```csharp
        Branches: Array.Empty<BranchInfo>());
```

becomes:

```csharp
        Branches: Array.Empty<BranchInfo>(),
        Tags: Array.Empty<TagInfo>(),
        Stashes: Array.Empty<StashInfo>());
```

`tests/GitHelper.Core.Tests/ActionCatalogTests.cs` — this one line lives inside `MinimalState()` (8-space indent); the same file also has four *positional* `RepoState` constructions handled separately in Step 5:

```csharp
        Branches: Array.Empty<BranchInfo>());
```

becomes:

```csharp
        Branches: Array.Empty<BranchInfo>(),
        Tags: Array.Empty<TagInfo>(),
        Stashes: Array.Empty<StashInfo>());
```

- [ ] **Step 5: Fix the four positional call sites in `ActionCatalogTests.cs`**

Four tests build `RepoState` positionally. Each of the four two-line blocks below is unique in the file (they differ in the numeric arguments), so each can be matched and replaced individually.

Block with `0, 0, true, true`:

```csharp
            @"C:\r", "main", false, "origin/main", 0, 0, true, true,
            Array.Empty<FileChange>(), Array.Empty<CommitInfo>(), Array.Empty<BranchInfo>());
```

becomes:

```csharp
            @"C:\r", "main", false, "origin/main", 0, 0, true, true,
            Array.Empty<FileChange>(), Array.Empty<CommitInfo>(), Array.Empty<BranchInfo>(),
            Array.Empty<TagInfo>(), Array.Empty<StashInfo>());
```

Block with `0, 1, true, true`:

```csharp
            @"C:\r", "main", false, "origin/main", 0, 1, true, true,
            Array.Empty<FileChange>(), Array.Empty<CommitInfo>(), Array.Empty<BranchInfo>());
```

becomes:

```csharp
            @"C:\r", "main", false, "origin/main", 0, 1, true, true,
            Array.Empty<FileChange>(), Array.Empty<CommitInfo>(), Array.Empty<BranchInfo>(),
            Array.Empty<TagInfo>(), Array.Empty<StashInfo>());
```

Block with `null, 0, 0, true, false`:

```csharp
            @"C:\r", "main", false, null, 0, 0, true, false,
            Array.Empty<FileChange>(), Array.Empty<CommitInfo>(), Array.Empty<BranchInfo>());
```

becomes:

```csharp
            @"C:\r", "main", false, null, 0, 0, true, false,
            Array.Empty<FileChange>(), Array.Empty<CommitInfo>(), Array.Empty<BranchInfo>(),
            Array.Empty<TagInfo>(), Array.Empty<StashInfo>());
```

Block with `1, 0, true, true`:

```csharp
            @"C:\r", "main", false, "origin/main", 1, 0, true, true,
            Array.Empty<FileChange>(), Array.Empty<CommitInfo>(), Array.Empty<BranchInfo>());
```

becomes:

```csharp
            @"C:\r", "main", false, "origin/main", 1, 0, true, true,
            Array.Empty<FileChange>(), Array.Empty<CommitInfo>(), Array.Empty<BranchInfo>(),
            Array.Empty<TagInfo>(), Array.Empty<StashInfo>());
```

- [ ] **Step 6: Fix `PreconditionTests.cs`**

Change:

```csharp
            Branches: new[] { new BranchInfo("main", "origin/main"), new BranchInfo("feature", null) });
```

to:

```csharp
            Branches: new[] { new BranchInfo("main", "origin/main"), new BranchInfo("feature", null) },
            Tags: Array.Empty<TagInfo>(),
            Stashes: Array.Empty<StashInfo>());
```

- [ ] **Step 7: Fix `BranchesViewModelTests.cs`**

Change:

```csharp
            Branches: branches.Length > 0 ? branches : new[] { new BranchInfo("main", upstream) });
```

to:

```csharp
            Branches: branches.Length > 0 ? branches : new[] { new BranchInfo("main", upstream) },
            Tags: Array.Empty<TagInfo>(),
            Stashes: Array.Empty<StashInfo>());
```

- [ ] **Step 8: Fix `TabViewTests.cs`**

This file uses fully-qualified names. Change:

```csharp
                Branches: Array.Empty<GitHelper.Core.Model.BranchInfo>()),
```

to:

```csharp
                Branches: Array.Empty<GitHelper.Core.Model.BranchInfo>(),
                Tags: Array.Empty<GitHelper.Core.Model.TagInfo>(),
                Stashes: Array.Empty<GitHelper.Core.Model.StashInfo>()),
```

- [ ] **Step 9: Build and run the full suite to confirm nothing else broke**

Run: `dotnet build GitHelper.sln`
Expected: builds with no errors.

Run: `dotnet test GitHelper.sln`
Expected: all existing tests still pass (same pass count as before this task — no behaviour changed, only the model shape).

- [ ] **Step 10: Commit**

```bash
git add src/GitHelper.Core/Model/TagInfo.cs src/GitHelper.Core/Model/StashInfo.cs src/GitHelper.Core/Model/RepoState.cs src/GitHelper.Core/Repo/RepoStateReader.cs tests/GitHelper.Core.Tests/ActionCatalogTests.cs tests/GitHelper.Core.Tests/SlotBinderTests.cs tests/GitHelper.Core.Tests/NarratorTests.cs tests/GitHelper.Core.Tests/PreconditionTests.cs tests/GitHelper.App.Tests/BranchesViewModelTests.cs tests/GitHelper.App.Tests/TabViewTests.cs tests/GitHelper.App.Tests/ChangesGitignoreBannerTests.cs tests/GitHelper.App.Tests/ChangesPushPromptTests.cs tests/GitHelper.App.Tests/ChangesConnectRemoteTests.cs tests/GitHelper.App.Tests/ChangesViewModelTests.cs
git commit -m "feat: add Tags and Stashes to RepoState"
```

---

### Task 2: `TagParser`

**Files:**
- Create: `src/GitHelper.Core/Parsing/TagParser.cs`
- Create: `tests/GitHelper.Core.Tests/TagParserTests.cs`

**Interfaces:**
- Consumes: `TagInfo(string Name, string Target)` from Task 1.
- Produces: `TagParser.Format` (string), `TagParser.Parse(string output) -> IReadOnlyList<TagInfo>`.

- [ ] **Step 1: Write the failing tests**

`tests/GitHelper.Core.Tests/TagParserTests.cs`:

```csharp
using GitHelper.Core.Parsing;

namespace GitHelper.Core.Tests;

public class TagParserTests
{
    [Fact]
    public void Parse_ReadsNameAndTarget()
    {
        var input = "v1\tabc1234\nv2\tdef5678\n";

        var tags = TagParser.Parse(input);

        Assert.Equal(2, tags.Count);
        Assert.Equal("v1", tags[0].Name);
        Assert.Equal("abc1234", tags[0].Target);
        Assert.Equal("v2", tags[1].Name);
        Assert.Equal("def5678", tags[1].Target);
    }

    [Fact]
    public void Parse_HandlesEmptyOutput()
    {
        Assert.Empty(TagParser.Parse(""));
    }

    [Fact]
    public async Task Parse_MatchesRealGitOutput()
    {
        using var repo = await TestRepo.CreateAsync();
        await repo.GitAsync("tag", "v1");

        var result = await repo.GitAsync("for-each-ref", "--format=" + TagParser.Format, "refs/tags/");
        var tags = TagParser.Parse(result.StdOut);

        Assert.Single(tags);
        Assert.Equal("v1", tags[0].Name);
        Assert.NotEmpty(tags[0].Target);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/GitHelper.Core.Tests --filter "FullyQualifiedName~TagParserTests"`
Expected: FAIL — `TagParser` does not exist yet (compile error).

- [ ] **Step 3: Implement `TagParser`**

`src/GitHelper.Core/Parsing/TagParser.cs`:

```csharp
using GitHelper.Core.Model;

namespace GitHelper.Core.Parsing;

/// <summary>Parses the tag format produced by <see cref="Format"/>.</summary>
public static class TagParser
{
    /// <summary>
    /// A tab is a safe separator here: git rejects control characters in refnames, so no
    /// tag name can contain one.
    /// </summary>
    public const string Format = "%(refname:short)%09%(objectname:short)";

    public static IReadOnlyList<TagInfo> Parse(string output)
    {
        var tags = new List<TagInfo>();

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.Length == 0) continue;

            var fields = trimmed.Split('\t');
            var name = fields[0];
            if (name.Length == 0) continue;

            var target = fields.Length > 1 ? fields[1] : string.Empty;
            tags.Add(new TagInfo(name, target));
        }

        return tags;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/GitHelper.Core.Tests --filter "FullyQualifiedName~TagParserTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/GitHelper.Core/Parsing/TagParser.cs tests/GitHelper.Core.Tests/TagParserTests.cs
git commit -m "feat: add TagParser"
```

---

### Task 3: `StashParser`

**Files:**
- Create: `src/GitHelper.Core/Parsing/StashParser.cs`
- Create: `tests/GitHelper.Core.Tests/StashParserTests.cs`

**Interfaces:**
- Consumes: `StashInfo(string Ref, string Message)` from Task 1.
- Produces: `StashParser.Format` (string), `StashParser.Parse(string output) -> IReadOnlyList<StashInfo>`.

- [ ] **Step 1: Write the failing tests**

`tests/GitHelper.Core.Tests/StashParserTests.cs`:

```csharp
using GitHelper.Core.Parsing;

namespace GitHelper.Core.Tests;

public class StashParserTests
{
    [Fact]
    public void Parse_ReadsRefAndMessage()
    {
        var input = "stash@{0}\tWIP on main: abc1234 first message\nstash@{1}\tOn main: second\n";

        var stashes = StashParser.Parse(input);

        Assert.Equal(2, stashes.Count);
        Assert.Equal("stash@{0}", stashes[0].Ref);
        Assert.Equal("WIP on main: abc1234 first message", stashes[0].Message);
        Assert.Equal("stash@{1}", stashes[1].Ref);
        Assert.Equal("On main: second", stashes[1].Message);
    }

    [Fact]
    public void Parse_HandlesEmptyOutput()
    {
        Assert.Empty(StashParser.Parse(""));
    }

    [Fact]
    public void Parse_KeepsAnyExtraTabsAsPartOfTheMessage()
    {
        // The subject is freeform text; only the first tab is the field separator.
        var input = "stash@{0}\tmessage\twith\ttabs\n";

        var stashes = StashParser.Parse(input);

        Assert.Equal("message\twith\ttabs", Assert.Single(stashes).Message);
    }

    [Fact]
    public async Task Parse_MatchesRealGitOutput()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("README.md", "changed\n");
        await repo.GitAsync("stash", "push", "-m", "wip");

        var result = await repo.GitAsync("stash", "list", "--format=" + StashParser.Format);
        var stashes = StashParser.Parse(result.StdOut);

        Assert.Single(stashes);
        Assert.StartsWith("stash@{0}", stashes[0].Ref);
        Assert.Contains("wip", stashes[0].Message);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/GitHelper.Core.Tests --filter "FullyQualifiedName~StashParserTests"`
Expected: FAIL — `StashParser` does not exist yet (compile error).

- [ ] **Step 3: Implement `StashParser`**

`src/GitHelper.Core/Parsing/StashParser.cs`:

```csharp
using GitHelper.Core.Model;

namespace GitHelper.Core.Parsing;

/// <summary>Parses the stash list format produced by <see cref="Format"/>.</summary>
public static class StashParser
{
    /// <summary>
    /// %gd is the reflog selector (e.g. "stash@{0}") and is what every stash action passes
    /// straight back to git, never re-derived from the entry's position. %s is the stash's
    /// own one-line subject. Only the first tab is treated as the field separator, because
    /// the subject is freeform text that could in principle contain one of its own.
    /// </summary>
    public const string Format = "%gd%x09%s";

    public static IReadOnlyList<StashInfo> Parse(string output)
    {
        var stashes = new List<StashInfo>();

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.Length == 0) continue;

            var tab = trimmed.IndexOf('\t');
            if (tab < 0) continue;

            var reference = trimmed[..tab];
            if (reference.Length == 0) continue;

            stashes.Add(new StashInfo(reference, trimmed[(tab + 1)..]));
        }

        return stashes;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/GitHelper.Core.Tests --filter "FullyQualifiedName~StashParserTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/GitHelper.Core/Parsing/StashParser.cs tests/GitHelper.Core.Tests/StashParserTests.cs
git commit -m "feat: add StashParser"
```

---

### Task 4: Wire `RepoStateReader` to read tags and stashes for real

**Files:**
- Modify: `src/GitHelper.Core/Repo/RepoStateReader.cs`
- Modify: `tests/GitHelper.Core.Tests/RepoStateReaderTests.cs`

**Interfaces:**
- Consumes: `TagParser.Format` / `.Parse`, `StashParser.Format` / `.Parse` from Tasks 2–3.

- [ ] **Step 1: Write the failing tests**

Add to `tests/GitHelper.Core.Tests/RepoStateReaderTests.cs` (inside the `RepoStateReaderTests` class, after `ReadAsync_ReportsCanUndoLastCommitOnlyWhenAParentExists`):

```csharp
    [Fact]
    public async Task ReadAsync_ReadsTags()
    {
        using var repo = await TestRepo.CreateAsync();
        await repo.GitAsync("tag", "v1");

        var state = await NewReader().ReadAsync(repo.Path);

        Assert.Single(state.Tags);
        Assert.Equal("v1", state.Tags[0].Name);
    }

    [Fact]
    public async Task ReadAsync_ReportsNoTagsWhenNoneExist()
    {
        using var repo = await TestRepo.CreateAsync();

        var state = await NewReader().ReadAsync(repo.Path);

        Assert.Empty(state.Tags);
    }

    [Fact]
    public async Task ReadAsync_ReadsStashes()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("README.md", "changed\n");
        await repo.GitAsync("stash", "push", "-m", "wip");

        var state = await NewReader().ReadAsync(repo.Path);

        Assert.Single(state.Stashes);
        Assert.Contains("wip", state.Stashes[0].Message);
    }

    [Fact]
    public async Task ReadAsync_ReportsNoStashesWhenNoneExist()
    {
        using var repo = await TestRepo.CreateAsync();

        var state = await NewReader().ReadAsync(repo.Path);

        Assert.Empty(state.Stashes);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/GitHelper.Core.Tests --filter "FullyQualifiedName~RepoStateReaderTests"`
Expected: FAIL — the new tests see empty `Tags`/`Stashes` because `RepoStateReader` still returns the Task 1 placeholders.

- [ ] **Step 3: Wire the real reads**

In `src/GitHelper.Core/Repo/RepoStateReader.cs`, change:

```csharp
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
            Branches: branches,
            Tags: Array.Empty<TagInfo>(),
            Stashes: Array.Empty<StashInfo>());
```

to:

```csharp
        var remoteResult = await runner.RunAsync(repoPath, new[] { "remote" }, ct);
        var hasRemote = remoteResult.Success && remoteResult.StdOut.Trim().Length > 0;

        var tagResult = await runner.RunAsync(
            repoPath, new[] { "for-each-ref", "--format=" + TagParser.Format, "refs/tags/" }, ct);
        var tags = TagParser.Parse(tagResult.StdOut);

        var stashResult = await runner.RunAsync(
            repoPath, new[] { "stash", "list", "--format=" + StashParser.Format }, ct);
        var stashes = StashParser.Parse(stashResult.StdOut);

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
            Branches: branches,
            Tags: tags,
            Stashes: stashes);
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/GitHelper.Core.Tests --filter "FullyQualifiedName~RepoStateReaderTests"`
Expected: PASS (all tests in the class, including the four new ones).

- [ ] **Step 5: Commit**

```bash
git add src/GitHelper.Core/Repo/RepoStateReader.cs tests/GitHelper.Core.Tests/RepoStateReaderTests.cs
git commit -m "feat: read tags and stashes into RepoState"
```

---

### Task 5: `ActionRequest` gains `TagName` and `StashRef`

**Files:**
- Modify: `src/GitHelper.Core/Actions/ActionRequest.cs`

**Interfaces:**
- Produces: `ActionRequest.TagName` (`string?`), `ActionRequest.StashRef` (`string?`) — both optional, appended after the existing fields so no existing call site (all of which use named arguments, or only the first few positional slots) breaks.

- [ ] **Step 1: Extend the record**

In `src/GitHelper.Core/Actions/ActionRequest.cs`, change:

```csharp
public sealed record ActionRequest(
    string ActionId,
    string? Path = null,
    string? Message = null,
    string? BranchName = null,
    string? RemoteUrl = null);
```

to:

```csharp
public sealed record ActionRequest(
    string ActionId,
    string? Path = null,
    string? Message = null,
    string? BranchName = null,
    string? RemoteUrl = null,
    string? TagName = null,
    string? StashRef = null);
```

- [ ] **Step 2: Build to confirm nothing broke**

Run: `dotnet build GitHelper.sln`
Expected: builds with no errors (new fields are optional, so every existing call site remains valid).

- [ ] **Step 3: Commit**

```bash
git add src/GitHelper.Core/Actions/ActionRequest.cs
git commit -m "feat: add TagName and StashRef to ActionRequest"
```

---

### Task 6: `SlotBinder` gains a `tagName` slot

**Files:**
- Modify: `src/GitHelper.Core/Content/SlotBinder.cs`
- Modify: `src/GitHelper.Core/Actions/ActionService.cs`
- Modify: `tests/GitHelper.Core.Tests/SlotBinderTests.cs`

**Interfaces:**
- Consumes: `ActionRequest.TagName` from Task 5.
- Produces: `SlotBinder.Bind(state, path, branchName, tagName, remoteUrl)` — note `tagName` is inserted before `remoteUrl`; the one production caller (`ActionService.PreviewAsync`) is updated in this task.

- [ ] **Step 1: Write the failing test**

Add to `tests/GitHelper.Core.Tests/SlotBinderTests.cs`, after `Bind_IncludesRequestValues`:

```csharp
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
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/GitHelper.Core.Tests --filter "FullyQualifiedName~SlotBinderTests"`
Expected: FAIL — `Bind` has no `tagName` parameter yet (compile error), and `KnownSlots_CoversEverySlotBindProduces` will also fail once it compiles, until Step 3 is done.

- [ ] **Step 3: Add the slot**

In `src/GitHelper.Core/Content/SlotBinder.cs`, change the `KnownSlots` set:

```csharp
    public static IReadOnlySet<string> KnownSlots { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "branch", "upstream", "ahead", "behind",
        "stagedCount", "unstagedCount", "untrackedCount",
        "stagedFileList", "path", "branchName", "repoName", "remoteUrl",
    };
```

to:

```csharp
    public static IReadOnlySet<string> KnownSlots { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "branch", "upstream", "ahead", "behind",
        "stagedCount", "unstagedCount", "untrackedCount",
        "stagedFileList", "path", "branchName", "repoName", "remoteUrl", "tagName",
    };
```

Then change the `Bind` signature and body:

```csharp
    public static IReadOnlyDictionary<string, string> Bind(
        RepoState state,
        string? path = null,
        string? branchName = null,
        string? remoteUrl = null)
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
            // Described rather than blank when absent: the panel previews connect-remote
            // before anything has been typed.
            ["remoteUrl"] = string.IsNullOrWhiteSpace(remoteUrl)
                ? "the address you paste"
                : remoteUrl.Trim(),
        };
    }
```

to:

```csharp
    public static IReadOnlyDictionary<string, string> Bind(
        RepoState state,
        string? path = null,
        string? branchName = null,
        string? tagName = null,
        string? remoteUrl = null)
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
            ["tagName"] = tagName ?? "the tag",
            ["repoName"] = new DirectoryInfo(state.RepoRoot).Name,
            // Described rather than blank when absent: the panel previews connect-remote
            // before anything has been typed.
            ["remoteUrl"] = string.IsNullOrWhiteSpace(remoteUrl)
                ? "the address you paste"
                : remoteUrl.Trim(),
        };
    }
```

- [ ] **Step 4: Update the one production caller**

In `src/GitHelper.Core/Actions/ActionService.cs`, change:

```csharp
        var slots = SlotBinder.Bind(
            state, request.Path, request.BranchName,
            blockers.Count == 0 ? request.RemoteUrl : null);
```

to:

```csharp
        var slots = SlotBinder.Bind(
            state, request.Path, request.BranchName, request.TagName,
            blockers.Count == 0 ? request.RemoteUrl : null);
```

- [ ] **Step 5: Run to verify everything passes**

Run: `dotnet test tests/GitHelper.Core.Tests --filter "FullyQualifiedName~SlotBinderTests"`
Expected: PASS (all tests, including `KnownSlots_CoversEverySlotBindProduces`).

Run: `dotnet build GitHelper.sln`
Expected: builds with no errors (the `ActionService` call site is the only other caller).

- [ ] **Step 6: Commit**

```bash
git add src/GitHelper.Core/Content/SlotBinder.cs src/GitHelper.Core/Actions/ActionService.cs tests/GitHelper.Core.Tests/SlotBinderTests.cs
git commit -m "feat: add a tagName content slot"
```

---

### Task 7: New preconditions for tags and stash

**Files:**
- Modify: `src/GitHelper.Core/Actions/Preconditions.cs`
- Modify: `tests/GitHelper.Core.Tests/PreconditionTests.cs`

**Interfaces:**
- Consumes: `RepoState.Tags`, `RepoState.HasUncommittedChanges` (already exists), `ActionRequest.TagName`, `ActionRequest.StashRef` from earlier tasks.
- Produces: `RequiresTagName`, `RequiresTagDoesNotExist`, `RequiresUncommittedChanges`, `RequiresStashRef` — all `IPrecondition` implementations, used by Tasks 8–9.

- [ ] **Step 1: Extend the test helpers, then write the failing tests**

In `tests/GitHelper.Core.Tests/PreconditionTests.cs`, change the `State` helper:

```csharp
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
            Branches: new[] { new BranchInfo("main", "origin/main"), new BranchInfo("feature", null) },
            Tags: Array.Empty<TagInfo>(),
            Stashes: Array.Empty<StashInfo>());
```

to:

```csharp
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
```

And the `Request` helper:

```csharp
    private static ActionRequest Request(
        string? path = null, string? message = null, string? branchName = null)
        => new("test", path, message, branchName);
```

to:

```csharp
    private static ActionRequest Request(
        string? path = null, string? message = null, string? branchName = null,
        string? tagName = null, string? stashRef = null)
        => new("test", path, message, branchName, TagName: tagName, StashRef: stashRef);
```

Then add these tests, after `RequiresBranchDoesNotExist_RefusesADuplicateName`:

```csharp
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
```

Finally, extend `EveryFailureMessageIsNonEmptyUserFacingCopy` to cover the new preconditions. Change:

```csharp
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
```

to:

```csharp
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
```

(`RequiresUncommittedChanges` is deliberately excluded from `all` here, the same way the comment above already excludes `RequiresBranchName` — this `failing` state has dirty changes, which is exactly what makes `RequiresUncommittedChanges` *pass*, not fail. `RequiresTagName` is excluded for the same reason: `tagName: "main"` is supplied so `RequiresTagDoesNotExist` and the branch-name-shaped `RequiresNotCurrentBranch"`-style checks can fail meaningfully.)

- [ ] **Step 2: Run to verify the new tests fail**

Run: `dotnet test tests/GitHelper.Core.Tests --filter "FullyQualifiedName~PreconditionTests"`
Expected: FAIL — `RequiresTagName`, `RequiresTagDoesNotExist`, `RequiresUncommittedChanges`, `RequiresStashRef` do not exist yet (compile error).

- [ ] **Step 3: Implement the four preconditions**

Add to `src/GitHelper.Core/Actions/Preconditions.cs`, after `RequiresBranchDoesNotExist`:

```csharp
public sealed class RequiresTagName : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
        => string.IsNullOrWhiteSpace(request.TagName)
            ? PreconditionResult.Fail("Type a name for the tag.")
            : PreconditionResult.Ok;
}

public sealed class RequiresTagDoesNotExist : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
        => state.Tags.Any(t => string.Equals(t.Name, request.TagName, StringComparison.Ordinal))
            ? PreconditionResult.Fail(
                $"A tag called '{request.TagName}' already exists. Pick a different name.")
            : PreconditionResult.Ok;
}

public sealed class RequiresUncommittedChanges : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
        => state.HasUncommittedChanges
            ? PreconditionResult.Ok
            : PreconditionResult.Fail(
                "There is nothing to set aside — nothing has changed since your last commit.");
}

public sealed class RequiresStashRef : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
        => string.IsNullOrWhiteSpace(request.StashRef)
            ? PreconditionResult.Fail("Pick a stash first — this action works on one at a time.")
            : PreconditionResult.Ok;
}
```

- [ ] **Step 4: Run to verify everything passes**

Run: `dotnet test tests/GitHelper.Core.Tests --filter "FullyQualifiedName~PreconditionTests"`
Expected: PASS (all tests in the class).

- [ ] **Step 5: Commit**

```bash
git add src/GitHelper.Core/Actions/Preconditions.cs tests/GitHelper.Core.Tests/PreconditionTests.cs
git commit -m "feat: add preconditions for tags and stash"
```

---

### Task 8: `ActionCatalog` entries for `create-tag` and `delete-tag`

**Files:**
- Modify: `src/GitHelper.Core/Actions/ActionCatalog.cs`
- Modify: `tests/GitHelper.Core.Tests/ActionCatalogTests.cs`

**Interfaces:**
- Consumes: `RequiresTagName`, `RequiresTagDoesNotExist`, `RequiresCommits` (existing) from Task 7.
- Produces: actions `create-tag` (`Danger.Safe`, undo `delete-tag`) and `delete-tag` (`Danger.Caution`).

- [ ] **Step 1: Write the failing tests**

In `tests/GitHelper.Core.Tests/ActionCatalogTests.cs`, change:

```csharp
    [Fact]
    public void All_ContainsExactlyTheFifteenActions()
    {
        var expected = new[]
        {
            "stage-file", "unstage-file", "stage-all", "unstage-all", "commit",
            "create-branch", "switch-branch", "fetch", "pull", "push",
            "discard-file", "undo-last-commit", "delete-branch",
            "connect-remote", "disconnect-remote",
        };

        Assert.Equal(expected.OrderBy(x => x), ActionCatalog.All.Select(a => a.Id).OrderBy(x => x));
    }
```

to:

```csharp
    [Fact]
    public void All_ContainsExactlyTheSeventeenActions()
    {
        var expected = new[]
        {
            "stage-file", "unstage-file", "stage-all", "unstage-all", "commit",
            "create-branch", "switch-branch", "fetch", "pull", "push",
            "discard-file", "undo-last-commit", "delete-branch",
            "connect-remote", "disconnect-remote",
            "create-tag", "delete-tag",
        };

        Assert.Equal(expected.OrderBy(x => x), ActionCatalog.All.Select(a => a.Id).OrderBy(x => x));
    }
```

Then add, after `ConnectRemote_ThenDisconnectRemote_RoundTripsAgainstARealRepository`:

```csharp
    [Fact]
    public void CreateTag_BuildsTagWithTheGivenName()
    {
        var args = ActionCatalog.Find("create-tag")!
            .BuildArgs(MinimalState(), new ActionRequest("create-tag", TagName: "v1"));

        Assert.Equal(new[] { "tag", "v1" }, args);
    }

    [Fact]
    public void DeleteTag_BuildsTagDashD()
    {
        var args = ActionCatalog.Find("delete-tag")!
            .BuildArgs(MinimalState(), new ActionRequest("delete-tag", TagName: "v1"));

        Assert.Equal(new[] { "tag", "-d", "v1" }, args);
    }

    [Fact]
    public void CreateTag_UndoesToDeleteTag()
    {
        Assert.Equal("delete-tag", ActionCatalog.Find("create-tag")!.UndoActionId);
    }

    [Fact]
    public async Task CreateTag_ThenDeleteTag_RoundTripsAgainstARealRepository()
    {
        using var repo = await TestRepo.CreateAsync();

        var tagged = await RunActionAsync(repo, new ActionRequest("create-tag", TagName: "v1"));
        Assert.Contains(tagged.Tags, t => t.Name == "v1");

        var untagged = await RunActionAsync(repo, new ActionRequest("delete-tag", TagName: "v1"));
        Assert.DoesNotContain(untagged.Tags, t => t.Name == "v1");
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/GitHelper.Core.Tests --filter "FullyQualifiedName~ActionCatalogTests"`
Expected: FAIL — `ActionCatalog.Find("create-tag")` and `Find("delete-tag")` return `null`, and the count test lists 17 expected ids against 15 actual.

- [ ] **Step 3: Add the two actions**

In `src/GitHelper.Core/Actions/ActionCatalog.cs`, add after the `disconnect-remote` entry, before the closing `};`:

```csharp

        new GitAction(
            Id: "create-tag",
            Title: "Tag this point",
            Danger: Danger.Safe,
            BuildArgs: (_, r) => new[] { "tag", r.TagName! },
            Preconditions: new IPrecondition[]
            {
                new RequiresTagName(), new RequiresCommits(), new RequiresTagDoesNotExist(),
            },
            UndoActionId: "delete-tag"),

        new GitAction(
            Id: "delete-tag",
            Title: "Delete tag",
            Danger: Danger.Caution,
            // Unlike branch -d, git has no refusal safety net here — tag -d always succeeds.
            BuildArgs: (_, r) => new[] { "tag", "-d", r.TagName! },
            Preconditions: new IPrecondition[] { new RequiresTagName() }),
```

- [ ] **Step 4: Run to verify everything passes**

Run: `dotnet test tests/GitHelper.Core.Tests --filter "FullyQualifiedName~ActionCatalogTests"`
Expected: FAIL still — `ContentIntegrityTests` is a different class and unaffected, but `ActionCatalogTests` itself should now PASS. (Content files for these two ids don't exist yet; that only affects `ContentIntegrityTests`, covered in Task 11. Run `dotnet test tests/GitHelper.Core.Tests --filter "FullyQualifiedName~ActionCatalogTests"` specifically and confirm PASS; a full `dotnet test` at this point will show `ContentIntegrityTests` failures, which is expected until Task 11.)

- [ ] **Step 5: Commit**

```bash
git add src/GitHelper.Core/Actions/ActionCatalog.cs tests/GitHelper.Core.Tests/ActionCatalogTests.cs
git commit -m "feat: add create-tag and delete-tag actions"
```

---

### Task 9: `ActionCatalog` entries for `stash`, `stash-pop`, `stash-apply`, `stash-drop`

**Files:**
- Modify: `src/GitHelper.Core/Actions/ActionCatalog.cs`
- Modify: `tests/GitHelper.Core.Tests/ActionCatalogTests.cs`

**Interfaces:**
- Consumes: `RequiresUncommittedChanges`, `RequiresStashRef`, `RequiresNoUncommittedChanges` (existing) from Task 7.
- Produces: actions `stash` (`Danger.Safe`, undo `stash-pop`), `stash-pop` (`Danger.Caution`), `stash-apply` (`Danger.Caution`), `stash-drop` (`Danger.Destructive`).

- [ ] **Step 1: Write the failing tests**

In `tests/GitHelper.Core.Tests/ActionCatalogTests.cs`, change the count test again:

```csharp
    [Fact]
    public void All_ContainsExactlyTheSeventeenActions()
    {
        var expected = new[]
        {
            "stage-file", "unstage-file", "stage-all", "unstage-all", "commit",
            "create-branch", "switch-branch", "fetch", "pull", "push",
            "discard-file", "undo-last-commit", "delete-branch",
            "connect-remote", "disconnect-remote",
            "create-tag", "delete-tag",
        };

        Assert.Equal(expected.OrderBy(x => x), ActionCatalog.All.Select(a => a.Id).OrderBy(x => x));
    }
```

to:

```csharp
    [Fact]
    public void All_ContainsExactlyTheTwentyOneActions()
    {
        var expected = new[]
        {
            "stage-file", "unstage-file", "stage-all", "unstage-all", "commit",
            "create-branch", "switch-branch", "fetch", "pull", "push",
            "discard-file", "undo-last-commit", "delete-branch",
            "connect-remote", "disconnect-remote",
            "create-tag", "delete-tag",
            "stash", "stash-pop", "stash-apply", "stash-drop",
        };

        Assert.Equal(expected.OrderBy(x => x), ActionCatalog.All.Select(a => a.Id).OrderBy(x => x));
    }
```

Change `DiscardFile_IsTheOnlyDestructiveActionInV1`:

```csharp
    [Fact]
    public void DiscardFile_IsTheOnlyDestructiveActionInV1()
    {
        var destructive = ActionCatalog.All.Where(a => a.Danger == Danger.Destructive).Select(a => a.Id);

        Assert.Equal(new[] { "discard-file" }, destructive);
    }
```

to:

```csharp
    [Fact]
    public void DiscardFileAndStashDrop_AreTheOnlyDestructiveActions()
    {
        var destructive = ActionCatalog.All.Where(a => a.Danger == Danger.Destructive).Select(a => a.Id);

        Assert.Equal(
            new[] { "discard-file", "stash-drop" }.OrderBy(x => x),
            destructive.OrderBy(x => x));
    }
```

Then add, after the tag round-trip test:

```csharp
    [Fact]
    public void Stash_BuildsStashPushWithoutAMessageWhenNoneGiven()
    {
        var args = ActionCatalog.Find("stash")!.BuildArgs(MinimalState(), new ActionRequest("stash"));

        Assert.Equal(new[] { "stash", "push" }, args);
    }

    [Fact]
    public void Stash_BuildsStashPushWithAMessageWhenOneIsGiven()
    {
        var args = ActionCatalog.Find("stash")!
            .BuildArgs(MinimalState(), new ActionRequest("stash", Message: "wip"));

        Assert.Equal(new[] { "stash", "push", "-m", "wip" }, args);
    }

    [Fact]
    public void StashPop_BuildsStashPopWithTheGivenRef()
    {
        var args = ActionCatalog.Find("stash-pop")!
            .BuildArgs(MinimalState(), new ActionRequest("stash-pop", StashRef: "stash@{0}"));

        Assert.Equal(new[] { "stash", "pop", "stash@{0}" }, args);
    }

    [Fact]
    public void StashApply_BuildsStashApplyWithTheGivenRef()
    {
        var args = ActionCatalog.Find("stash-apply")!
            .BuildArgs(MinimalState(), new ActionRequest("stash-apply", StashRef: "stash@{0}"));

        Assert.Equal(new[] { "stash", "apply", "stash@{0}" }, args);
    }

    [Fact]
    public void StashDrop_BuildsStashDropWithTheGivenRef()
    {
        var args = ActionCatalog.Find("stash-drop")!
            .BuildArgs(MinimalState(), new ActionRequest("stash-drop", StashRef: "stash@{0}"));

        Assert.Equal(new[] { "stash", "drop", "stash@{0}" }, args);
    }

    [Fact]
    public void Stash_UndoesToStashPop()
    {
        Assert.Equal("stash-pop", ActionCatalog.Find("stash")!.UndoActionId);
    }

    [Fact]
    public async Task Stash_ThenStashPop_RoundTripsAgainstARealRepository()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("README.md", "changed\n");

        var stashed = await RunActionAsync(repo, new ActionRequest("stash", Message: "wip"));
        Assert.Single(stashed.Stashes);
        Assert.False(stashed.HasUncommittedChanges);

        var popped = await RunActionAsync(
            repo, new ActionRequest("stash-pop", StashRef: stashed.Stashes[0].Ref));
        Assert.Empty(popped.Stashes);
        Assert.True(popped.HasUncommittedChanges);
    }

    [Fact]
    public async Task Stash_ThenStashDrop_RemovesTheEntryWithoutRestoringChanges()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("README.md", "changed\n");

        var stashed = await RunActionAsync(repo, new ActionRequest("stash", Message: "wip"));

        var dropped = await RunActionAsync(
            repo, new ActionRequest("stash-drop", StashRef: stashed.Stashes[0].Ref));
        Assert.Empty(dropped.Stashes);
        Assert.False(dropped.HasUncommittedChanges);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/GitHelper.Core.Tests --filter "FullyQualifiedName~ActionCatalogTests"`
Expected: FAIL — the four stash action ids resolve to `null`.

- [ ] **Step 3: Add the four actions**

In `src/GitHelper.Core/Actions/ActionCatalog.cs`, add after the `delete-tag` entry from Task 8, before the closing `};`:

```csharp

        new GitAction(
            Id: "stash",
            Title: "Set changes aside",
            Danger: Danger.Safe,
            BuildArgs: (_, r) => string.IsNullOrWhiteSpace(r.Message)
                ? new[] { "stash", "push" }
                : new[] { "stash", "push", "-m", r.Message! },
            Preconditions: new IPrecondition[] { new RequiresUncommittedChanges() },
            UndoActionId: "stash-pop"),

        new GitAction(
            Id: "stash-pop",
            Title: "Bring back stashed changes",
            Danger: Danger.Caution,
            BuildArgs: (_, r) => new[] { "stash", "pop", r.StashRef! },
            // Only offered against a clean tree, so this can never land on other unsaved
            // edits and conflict with them -- the app has no operation-state model yet.
            Preconditions: new IPrecondition[] { new RequiresStashRef(), new RequiresNoUncommittedChanges() }),

        new GitAction(
            Id: "stash-apply",
            Title: "Copy back stashed changes",
            Danger: Danger.Caution,
            BuildArgs: (_, r) => new[] { "stash", "apply", r.StashRef! },
            Preconditions: new IPrecondition[] { new RequiresStashRef(), new RequiresNoUncommittedChanges() }),

        new GitAction(
            Id: "stash-drop",
            Title: "Delete stash",
            Danger: Danger.Destructive,
            BuildArgs: (_, r) => new[] { "stash", "drop", r.StashRef! },
            Preconditions: new IPrecondition[] { new RequiresStashRef() }),
```

- [ ] **Step 4: Run to verify everything passes**

Run: `dotnet test tests/GitHelper.Core.Tests --filter "FullyQualifiedName~ActionCatalogTests"`
Expected: PASS (all tests in the class).

- [ ] **Step 5: Commit**

```bash
git add src/GitHelper.Core/Actions/ActionCatalog.cs tests/GitHelper.Core.Tests/ActionCatalogTests.cs
git commit -m "feat: add stash, stash-pop, stash-apply, and stash-drop actions"
```

---

### Task 10: Translate "no stash entries found"

The one failure mode Tasks 8–9 don't already prevent structurally: a stash targeted by a stale ref (dropped elsewhere, e.g. the CLI in another window) between preview and run.

**Files:**
- Modify: `src/GitHelper.Core/Errors/ErrorTranslator.cs`
- Modify: `tests/GitHelper.Core.Tests/ErrorTranslatorTests.cs`

- [ ] **Step 1: Write the failing test**

In `tests/GitHelper.Core.Tests/ErrorTranslatorTests.cs`, add a row to the `Translate_RecognisesKnownFailures` theory:

```csharp
    [InlineData("fatal: Not possible to fast-forward, aborting.", "fast-forward")]
    // Real git output, verbatim: what a beginner hits committing in a freshly `git init`-ed
    // repository with no global identity set.
    [InlineData("*** Please tell me who you are.\n\nfatal: unable to auto-detect email address", "who you are")]
    [InlineData("fatal: No stash entries found.", "stash")]
    public void Translate_RecognisesKnownFailures(string stdErr, string expectedFragment)
```

(Only the new `[InlineData]` line is added; the surrounding attributes and method signature stay as they are — reproduced here so the insertion point is unambiguous.)

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/GitHelper.Core.Tests --filter "FullyQualifiedName~ErrorTranslatorTests"`
Expected: FAIL — "No stash entries found." falls through to the generic "I don't have a plain-English explanation" rule, whose summary and explanation don't contain "stash".

- [ ] **Step 3: Add the rule**

In `src/GitHelper.Core/Errors/ErrorTranslator.cs`, add to the `Rules` array, after the `"pathspec"` rule (last one before the closing `};`):

```csharp
        new("no stash entries found",
            "That stash is no longer there",
            "There is nothing stashed right now. It may already have been brought back, "
            + "deleted, or removed from outside this app.",
            new[] { "Refresh and check the list again." }),
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/GitHelper.Core.Tests --filter "FullyQualifiedName~ErrorTranslatorTests"`
Expected: PASS (all rows of the theory, including the new one).

- [ ] **Step 5: Commit**

```bash
git add src/GitHelper.Core/Errors/ErrorTranslator.cs tests/GitHelper.Core.Tests/ErrorTranslatorTests.cs
git commit -m "feat: translate a stash entry that no longer exists"
```

---

### Task 11: Content for `create-tag` and `delete-tag`, plus the `tag` glossary term

**Files:**
- Create: `src/GitHelper.Content/actions/create-tag.md`
- Create: `src/GitHelper.Content/actions/delete-tag.md`
- Create: `src/GitHelper.Content/terms/tag.md`

**Interfaces:**
- Consumes: `ActionCatalog` entries `create-tag` / `delete-tag` from Task 8 (frontmatter `danger`/`undo` must match `Danger.Safe`/`"delete-tag"` and `Danger.Caution`/no undo, respectively — `ContentIntegrityTests.FrontmatterDangerMatchesTheActionDescriptor` and `FrontmatterUndoMatchesTheActionDescriptor` check this exactly).

No new test file: `tests/GitHelper.Core.Tests/ContentIntegrityTests.cs` already generically validates every action's content file, every declared and inline `[[term]]` reference, and every `{slot}` — these three files are what make it pass for `create-tag`/`delete-tag`.

- [ ] **Step 1: Confirm the tests currently fail because of these two actions**

Run: `dotnet test tests/GitHelper.Core.Tests --filter "FullyQualifiedName~ContentIntegrityTests"`
Expected: FAIL — `EveryActionHasAContentFile` lists `create-tag` and `delete-tag` as missing.

- [ ] **Step 2: Write `src/GitHelper.Content/terms/tag.md`**

```markdown
---
id: tag
title: tag
---
## definition
A fixed label pointing at one commit. Unlike a [[branch]], a tag never moves once you
create it — useful for marking one exact point, like a release, so you can find it again
later.
```

- [ ] **Step 3: Write `src/GitHelper.Content/actions/create-tag.md`**

```markdown
---
id: create-tag
title: Tag this point
danger: safe
terms: [tag, branch, commit]
undo: delete-tag
---
## what
Marks the [[commit]] you are on right now with the label {tagName}. Unlike a [[branch]], a
[[tag]] never moves — it keeps pointing at this exact commit even after you keep working and
make new ones.

This only tags the commit you currently have checked out. This version of the app has no
way to pick an earlier commit to tag instead.

## risks
Nothing else changes. No files are touched and no commit is created — this only adds a
named pointer.

A tag name has to be unique: git will not let you reuse one that already exists here.

## undo
Deleting the tag removes the label. The commit it pointed to, and everything on it, stays
exactly as it was.
```

- [ ] **Step 4: Write `src/GitHelper.Content/actions/delete-tag.md`**

```markdown
---
id: delete-tag
title: Delete tag
danger: caution
terms: [tag, branch, commit]
---
## what
Removes the label {tagName}. Unlike deleting a [[branch]], git does not check first whether
anything else still needs it — a [[tag]] is just a name, so removing it always succeeds.

## risks
The [[commit]] the tag pointed to is not affected. Only the name goes away.

If this tag has already been shared — for example, pushed to GitHub — removing it here does
not remove it there. This app only manages tags on this computer.

## undo
There is no undo button for this. If you still know which commit it pointed to, you can
create a new tag with the same name pointing at it again.
```

- [ ] **Step 5: Run to verify the content tests now pass**

Run: `dotnet test tests/GitHelper.Core.Tests --filter "FullyQualifiedName~ContentIntegrityTests"`
Expected: PASS for everything concerning `create-tag`/`delete-tag`/`tag`. (Two stash-related failures remain — `stash`, `stash-pop`, `stash-apply`, `stash-drop` still have no content files; that's Task 12.)

- [ ] **Step 6: Commit**

```bash
git add src/GitHelper.Content/actions/create-tag.md src/GitHelper.Content/actions/delete-tag.md src/GitHelper.Content/terms/tag.md
git commit -m "docs: add content for create-tag and delete-tag"
```

---

### Task 12: Content for the four stash actions, plus the `stash` glossary term

**Files:**
- Create: `src/GitHelper.Content/actions/stash.md`
- Create: `src/GitHelper.Content/actions/stash-pop.md`
- Create: `src/GitHelper.Content/actions/stash-apply.md`
- Create: `src/GitHelper.Content/actions/stash-drop.md`
- Create: `src/GitHelper.Content/terms/stash.md`

**Interfaces:**
- Consumes: `ActionCatalog` entries `stash` / `stash-pop` / `stash-apply` / `stash-drop` from Task 9 (frontmatter `danger`/`undo` must match exactly).

- [ ] **Step 1: Confirm `ContentIntegrityTests` currently fails because of these four actions**

Run: `dotnet test tests/GitHelper.Core.Tests --filter "FullyQualifiedName~ContentIntegrityTests"`
Expected: FAIL — `EveryActionHasAContentFile` lists `stash`, `stash-pop`, `stash-apply`, `stash-drop` as missing.

- [ ] **Step 2: Write `src/GitHelper.Content/terms/stash.md`**

```markdown
---
id: stash
title: stash
---
## definition
A shelf for changes you are not ready to commit. Stashing lifts your edits out of the way
without saving them as a commit, so you get back a clean [[working-directory|working
directory]] and can bring the changes back later.
```

- [ ] **Step 3: Write `src/GitHelper.Content/actions/stash.md`**

```markdown
---
id: stash
title: Set changes aside
danger: safe
terms: [stash, working-directory, commit]
undo: stash-pop
---
## what
Lifts your uncommitted changes off to the side and gives you back a clean
[[working-directory|working directory]], without creating a [[commit]]. Git calls this
shelf a [[stash]].

Only changes to files git already tracks are set aside. Files you have never staged or
committed are left exactly where they are.

## risks
Nothing is lost, but it is out of sight: a file that looks unchanged after this has its
edits sitting in the stash, not gone. Bringing them back is how you see them again.

## undo
Bringing the changes back restores them and removes this entry from the stash list.
```

- [ ] **Step 4: Write `src/GitHelper.Content/actions/stash-pop.md`**

```markdown
---
id: stash-pop
title: Bring back stashed changes
danger: caution
terms: [stash, working-directory]
---
## what
Copies the changes from this [[stash]] entry back into your [[working-directory|working
directory]] and removes the entry from the list — the shelf and the changes on it, put
back at the same time.

## risks
Only offered when your [[working-directory|working directory]] is clean, so this can never
land on top of other unsaved edits and conflict with them.

## undo
There is no undo button for this specific step, but the change it makes is exactly the
opposite of stashing — setting the same changes aside again gets back to where you started.
```

- [ ] **Step 5: Write `src/GitHelper.Content/actions/stash-apply.md`**

```markdown
---
id: stash-apply
title: Copy back stashed changes
danger: caution
terms: [stash, working-directory]
---
## what
Copies the changes from this [[stash]] entry back into your [[working-directory|working
directory]], the same as bringing them back, but leaves the entry on the list afterwards
instead of removing it.

Useful when you want the same changes in more than one place without giving up the copy on
the shelf.

## risks
Only offered when your [[working-directory|working directory]] is clean, so this can never
land on top of other unsaved edits and conflict with them.

## undo
There is no undo button for this specific step. Deleting the stash afterwards removes the
shelved copy; the changes just applied to your files are unaffected by that.
```

- [ ] **Step 6: Write `src/GitHelper.Content/actions/stash-drop.md`**

```markdown
---
id: stash-drop
title: Delete stash
danger: destructive
terms: [stash, commit]
---
## what
Removes this entry from the [[stash]] list for good, without copying its changes back
anywhere first.

## risks
This is the one way stashed work actually disappears. Once dropped, there is no file,
[[commit]], or list entry left holding those changes — they cannot be recovered through
this app.

## undo
There is no undo. Bring the changes back first if there is any chance you still want them.
```

- [ ] **Step 7: Run to verify all content tests now pass**

Run: `dotnet test tests/GitHelper.Core.Tests --filter "FullyQualifiedName~ContentIntegrityTests"`
Expected: PASS (every test in the class).

Run: `dotnet test GitHelper.sln`
Expected: PASS — the whole `GitHelper.Core.Tests` project should now be green.

- [ ] **Step 8: Commit**

```bash
git add src/GitHelper.Content/actions/stash.md src/GitHelper.Content/actions/stash-pop.md src/GitHelper.Content/actions/stash-apply.md src/GitHelper.Content/actions/stash-drop.md src/GitHelper.Content/terms/stash.md
git commit -m "docs: add content for stash, stash-pop, stash-apply, and stash-drop"
```

---

### Task 13: `TagRowViewModel` and the Tags section of `BranchesViewModel`

**Files:**
- Create: `src/GitHelper.App/ViewModels/TagRowViewModel.cs`
- Modify: `src/GitHelper.App/ViewModels/BranchesViewModel.cs`
- Modify: `tests/GitHelper.App.Tests/BranchesViewModelTests.cs`

**Interfaces:**
- Consumes: `RepoState.Tags`, action ids `create-tag` / `delete-tag` from earlier tasks.
- Produces: `TagRowViewModel(TagInfo, Func<string,string,Task>)` with `Name`, `TargetLabel`, `DeleteCommand`; `BranchesViewModel.Tags` (`ObservableCollection<TagRowViewModel>`), `.NewTagName` (`string`, two-way bindable), `.CreateTagCommand`.

- [ ] **Step 1: Write the failing tests**

In `tests/GitHelper.App.Tests/BranchesViewModelTests.cs`, extend the `State` helper to accept tags:

```csharp
    private static RepoState State(
        string? branch = "main",
        bool isDetached = false,
        string? upstream = null,
        int ahead = 0,
        int behind = 0,
        bool hasRemote = false,
        params BranchInfo[] branches)
        => new(
            RepoRoot: @"C:\r", Branch: branch, IsDetached: isDetached, Upstream: upstream,
            Ahead: ahead, Behind: behind, HasCommits: true, HasRemote: hasRemote,
            Changes: Array.Empty<FileChange>(),
            RecentCommits: Array.Empty<CommitInfo>(),
            Branches: branches.Length > 0 ? branches : new[] { new BranchInfo("main", upstream) },
            Tags: Array.Empty<TagInfo>(),
            Stashes: Array.Empty<StashInfo>());
```

to:

```csharp
    private static RepoState State(
        string? branch = "main",
        bool isDetached = false,
        string? upstream = null,
        int ahead = 0,
        int behind = 0,
        bool hasRemote = false,
        TagInfo[]? tags = null,
        params BranchInfo[] branches)
        => new(
            RepoRoot: @"C:\r", Branch: branch, IsDetached: isDetached, Upstream: upstream,
            Ahead: ahead, Behind: behind, HasCommits: true, HasRemote: hasRemote,
            Changes: Array.Empty<FileChange>(),
            RecentCommits: Array.Empty<CommitInfo>(),
            Branches: branches.Length > 0 ? branches : new[] { new BranchInfo("main", upstream) },
            Tags: tags ?? Array.Empty<TagInfo>(),
            Stashes: Array.Empty<StashInfo>());
```

Then add these tests, after `Update_ReplacesRowsRatherThanAccumulatingThem`:

```csharp
    [Fact]
    public void Update_ListsTagsWithTheirTarget()
    {
        var f = NewFixture();

        f.Branches.Update(State(tags: new[] { new TagInfo("v1", "abc1234") }));

        var row = Assert.Single(f.Branches.Tags);
        Assert.Equal("v1", row.Name);
        Assert.Equal("abc1234", row.TargetLabel);
    }

    [Fact]
    public async Task CreateTagCommand_ThenListedInTags()
    {
        using var repo = await TestRepo.CreateAsync();
        var f = NewFixture();
        f.Branches.Update(await f.Reader.ReadAsync(repo.Path));
        f.Branches.NewTagName = "v1";

        // create-tag is Safe, so ShowAndRunIfUngated runs it straight away.
        await f.Branches.CreateTagCommand.ExecuteAsync(null);

        var after = await f.Reader.ReadAsync(repo.Path);
        Assert.Contains(after.Tags, t => t.Name == "v1");
    }

    [Fact]
    public async Task TagDeleteCommand_ThenConfirming_RemovesIt()
    {
        using var repo = await TestRepo.CreateAsync();
        await repo.GitAsync("tag", "v1");
        var f = NewFixture();
        f.Branches.Update(await f.Reader.ReadAsync(repo.Path));

        await f.Branches.Tags.Single(t => t.Name == "v1").DeleteCommand.ExecuteAsync(null);
        await f.Panel.RunAsync();

        var after = await f.Reader.ReadAsync(repo.Path);
        Assert.DoesNotContain(after.Tags, t => t.Name == "v1");
    }

    [Fact]
    public void OnActionCompleted_ClearsTheTagNameBoxOnlyWhenThatTagAppeared()
    {
        var f = NewFixture();
        f.Branches.NewTagName = "v1";

        var before = State(tags: Array.Empty<TagInfo>());
        var after = State(tags: new[] { new TagInfo("v1", "abc1234") });

        f.Branches.OnActionCompleted(OutcomeBetween(before, after));

        Assert.Equal(string.Empty, f.Branches.NewTagName);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/GitHelper.App.Tests --filter "FullyQualifiedName~BranchesViewModelTests"`
Expected: FAIL — `TagInfo`/`TagRowViewModel`/`Tags`/`NewTagName`/`CreateTagCommand` do not exist on `BranchesViewModel` yet (compile error).

- [ ] **Step 3: Create `TagRowViewModel`**

`src/GitHelper.App/ViewModels/TagRowViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.Input;
using GitHelper.Core.Model;

namespace GitHelper.App.ViewModels;

/// <summary>One tag in the Branches view.</summary>
public sealed class TagRowViewModel : ViewModelBase
{
    public TagRowViewModel(TagInfo tag, Func<string, string, Task> invokeAction)
    {
        Name = tag.Name;
        TargetLabel = tag.Target;

        DeleteCommand = new AsyncRelayCommand(() => invokeAction("delete-tag", tag.Name));
    }

    public string Name { get; }

    public string TargetLabel { get; }

    public IAsyncRelayCommand DeleteCommand { get; }
}
```

- [ ] **Step 4: Wire it into `BranchesViewModel`**

In `src/GitHelper.App/ViewModels/BranchesViewModel.cs`, change the constructor:

```csharp
    public BranchesViewModel(ExplainPanelViewModel explain)
    {
        _explain = explain;

        CreateBranchCommand = new AsyncRelayCommand(
            () => InvokeAsync("create-branch", branchName: NewBranchName));
        FetchCommand = new AsyncRelayCommand(() => InvokeAsync("fetch"));
        PullCommand = new AsyncRelayCommand(() => InvokeAsync("pull"));
        DisconnectRemoteCommand = new AsyncRelayCommand(() => InvokeAsync("disconnect-remote"));
    }
```

to:

```csharp
    public BranchesViewModel(ExplainPanelViewModel explain)
    {
        _explain = explain;

        CreateBranchCommand = new AsyncRelayCommand(
            () => InvokeAsync("create-branch", branchName: NewBranchName));
        FetchCommand = new AsyncRelayCommand(() => InvokeAsync("fetch"));
        PullCommand = new AsyncRelayCommand(() => InvokeAsync("pull"));
        DisconnectRemoteCommand = new AsyncRelayCommand(() => InvokeAsync("disconnect-remote"));
        CreateTagCommand = new AsyncRelayCommand(() => InvokeAsync("create-tag", tagName: NewTagName));
    }
```

Add the collection and property, after `public ObservableCollection<BranchRowViewModel> Branches { get; } = new();`:

```csharp
    public ObservableCollection<BranchRowViewModel> Branches { get; } = new();

    public ObservableCollection<TagRowViewModel> Tags { get; } = new();
```

Add `NewTagName` next to `NewBranchName`:

```csharp
    [ObservableProperty] private string _newBranchName = string.Empty;
```

becomes:

```csharp
    [ObservableProperty] private string _newBranchName = string.Empty;
    [ObservableProperty] private string _newTagName = string.Empty;
```

Add the command property, after `public IAsyncRelayCommand CreateBranchCommand { get; }`:

```csharp
    public IAsyncRelayCommand CreateBranchCommand { get; }

    public IAsyncRelayCommand CreateTagCommand { get; }
```

Populate `Tags` in `Update`, and add the tag-clearing check to `OnActionCompleted`. Change:

```csharp
    public void Update(RepoState state)
    {
        _repoPath = state.RepoRoot;

        Branches.Clear();
        foreach (var branch in state.Branches)
        {
            var isCurrent = !state.IsDetached
                && string.Equals(branch.Name, state.Branch, StringComparison.Ordinal);
            Branches.Add(new BranchRowViewModel(branch, isCurrent, InvokeWithBranchAsync));
        }

        IsDetached = state.IsDetached;
        HasRemote = state.HasRemote;
        CurrentBranchLabel = state.IsDetached
            // Told plainly rather than shown as a blank branch name.
            ? "You are not on a branch (git calls this a detached HEAD)"
            : state.Branch ?? "no branch";
        SyncSummary = DescribeSync(state);
    }
```

to:

```csharp
    public void Update(RepoState state)
    {
        _repoPath = state.RepoRoot;

        Branches.Clear();
        foreach (var branch in state.Branches)
        {
            var isCurrent = !state.IsDetached
                && string.Equals(branch.Name, state.Branch, StringComparison.Ordinal);
            Branches.Add(new BranchRowViewModel(branch, isCurrent, InvokeWithBranchAsync));
        }

        Tags.Clear();
        foreach (var tag in state.Tags)
            Tags.Add(new TagRowViewModel(tag, InvokeWithTagAsync));

        IsDetached = state.IsDetached;
        HasRemote = state.HasRemote;
        CurrentBranchLabel = state.IsDetached
            // Told plainly rather than shown as a blank branch name.
            ? "You are not on a branch (git calls this a detached HEAD)"
            : state.Branch ?? "no branch";
        SyncSummary = DescribeSync(state);
    }
```

Change `OnActionCompleted`:

```csharp
    public void OnActionCompleted(ActionOutcome outcome)
    {
        if (!outcome.Success || string.IsNullOrEmpty(NewBranchName)) return;

        var existedBefore = outcome.Before.Branches.Any(
            b => string.Equals(b.Name, NewBranchName, StringComparison.Ordinal));
        var existsAfter = outcome.After.Branches.Any(
            b => string.Equals(b.Name, NewBranchName, StringComparison.Ordinal));

        if (!existedBefore && existsAfter) NewBranchName = string.Empty;
    }
```

to:

```csharp
    public void OnActionCompleted(ActionOutcome outcome)
    {
        if (!outcome.Success) return;

        if (!string.IsNullOrEmpty(NewBranchName))
        {
            var existedBefore = outcome.Before.Branches.Any(
                b => string.Equals(b.Name, NewBranchName, StringComparison.Ordinal));
            var existsAfter = outcome.After.Branches.Any(
                b => string.Equals(b.Name, NewBranchName, StringComparison.Ordinal));

            if (!existedBefore && existsAfter) NewBranchName = string.Empty;
        }

        if (!string.IsNullOrEmpty(NewTagName))
        {
            var existedBefore = outcome.Before.Tags.Any(
                t => string.Equals(t.Name, NewTagName, StringComparison.Ordinal));
            var existsAfter = outcome.After.Tags.Any(
                t => string.Equals(t.Name, NewTagName, StringComparison.Ordinal));

            if (!existedBefore && existsAfter) NewTagName = string.Empty;
        }
    }
```

Finally, add the helper method, after `InvokeWithBranchAsync`:

```csharp
    private Task InvokeWithBranchAsync(string actionId, string branchName)
        => InvokeAsync(actionId, branchName);

    private Task InvokeWithTagAsync(string actionId, string tagName)
        => InvokeAsync(actionId, tagName: tagName);
```

and extend `InvokeAsync` to accept it:

```csharp
    private Task InvokeAsync(string actionId, string? branchName = null)
        => _repoPath is null
            ? Task.CompletedTask
            : _explain.ShowAndRunIfUngatedAsync(
                _repoPath, new ActionRequest(actionId, BranchName: branchName));
```

to:

```csharp
    private Task InvokeAsync(string actionId, string? branchName = null, string? tagName = null)
        => _repoPath is null
            ? Task.CompletedTask
            : _explain.ShowAndRunIfUngatedAsync(
                _repoPath, new ActionRequest(actionId, BranchName: branchName, TagName: tagName));
```

- [ ] **Step 5: Run to verify everything passes**

Run: `dotnet test tests/GitHelper.App.Tests --filter "FullyQualifiedName~BranchesViewModelTests"`
Expected: PASS (all tests in the class).

- [ ] **Step 6: Commit**

```bash
git add src/GitHelper.App/ViewModels/TagRowViewModel.cs src/GitHelper.App/ViewModels/BranchesViewModel.cs tests/GitHelper.App.Tests/BranchesViewModelTests.cs
git commit -m "feat: list and manage tags on the Branches tab"
```

---

### Task 14: Tags section in `BranchesView.axaml`

**Files:**
- Modify: `src/GitHelper.App/Views/BranchesView.axaml`
- Modify: `tests/GitHelper.App.Tests/TabViewTests.cs`

**Interfaces:**
- Consumes: `BranchesViewModel.Tags`, `.NewTagName`, `.CreateTagCommand`, `TagRowViewModel.Name`/`.TargetLabel`/`.DeleteCommand` from Task 13.

- [ ] **Step 1: Write the failing test**

In `tests/GitHelper.App.Tests/TabViewTests.cs`, add after `BranchesView_BindsTheNewBranchNameBoxBothWays`:

```csharp
    [AvaloniaFact]
    public async Task BranchesView_ShowsTagRowsAndBindsTheNewTagNameBox()
    {
        using var repo = await TestRepo.CreateAsync();
        await repo.GitAsync("tag", "v1");
        var reader = new RepoStateReader(new GitRunner());
        var vm = new BranchesViewModel(NewPanel());
        vm.Update(await reader.ReadAsync(repo.Path));

        var view = new BranchesView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();

        Assert.NotNull(view.FindControl<ItemsControl>("TagsHost"));
        var box = view.FindControl<TextBox>("NewTagNameBox");
        Assert.NotNull(box);
        Assert.Single(vm.Tags);

        box!.Text = "v2";
        Assert.Equal("v2", vm.NewTagName);
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/GitHelper.App.Tests --filter "FullyQualifiedName~TabViewTests"`
Expected: FAIL — `TagsHost` and `NewTagNameBox` are not in the view yet.

- [ ] **Step 3: Add the Tags section**

In `src/GitHelper.App/Views/BranchesView.axaml`, change:

```xml
      <!-- All branches -->
      <StackPanel Spacing="6">
        <TextBlock Text="Branches" FontWeight="Bold" />
        <ItemsControl Name="BranchesHost" ItemsSource="{Binding Branches}">
          <ItemsControl.ItemTemplate>
            <DataTemplate x:DataType="vm:BranchRowViewModel">
              <Grid ColumnDefinitions="*,Auto,Auto" Margin="0,3">
                <StackPanel Spacing="2">
                  <StackPanel Orientation="Horizontal" Spacing="6">
                    <TextBlock Text="{Binding Name}" FontWeight="SemiBold" />
                    <TextBlock Text="(you are here)" Opacity="0.6" FontSize="12"
                               IsVisible="{Binding IsCurrent}" VerticalAlignment="Center" />
                  </StackPanel>
                  <TextBlock Text="{Binding UpstreamLabel}" Opacity="0.6" FontSize="12" />
                </StackPanel>
                <Button Grid.Column="1" Content="Switch to it"
                        Command="{Binding SwitchCommand}" IsVisible="{Binding CanSwitch}" />
                <Button Grid.Column="2" Content="Delete"
                        Command="{Binding DeleteCommand}" IsVisible="{Binding CanDelete}"
                        Margin="6,0,0,0" />
              </Grid>
            </DataTemplate>
          </ItemsControl.ItemTemplate>
        </ItemsControl>
      </StackPanel>

    </StackPanel>
  </ScrollViewer>
</UserControl>
```

to:

```xml
      <!-- All branches -->
      <StackPanel Spacing="6">
        <TextBlock Text="Branches" FontWeight="Bold" />
        <ItemsControl Name="BranchesHost" ItemsSource="{Binding Branches}">
          <ItemsControl.ItemTemplate>
            <DataTemplate x:DataType="vm:BranchRowViewModel">
              <Grid ColumnDefinitions="*,Auto,Auto" Margin="0,3">
                <StackPanel Spacing="2">
                  <StackPanel Orientation="Horizontal" Spacing="6">
                    <TextBlock Text="{Binding Name}" FontWeight="SemiBold" />
                    <TextBlock Text="(you are here)" Opacity="0.6" FontSize="12"
                               IsVisible="{Binding IsCurrent}" VerticalAlignment="Center" />
                  </StackPanel>
                  <TextBlock Text="{Binding UpstreamLabel}" Opacity="0.6" FontSize="12" />
                </StackPanel>
                <Button Grid.Column="1" Content="Switch to it"
                        Command="{Binding SwitchCommand}" IsVisible="{Binding CanSwitch}" />
                <Button Grid.Column="2" Content="Delete"
                        Command="{Binding DeleteCommand}" IsVisible="{Binding CanDelete}"
                        Margin="6,0,0,0" />
              </Grid>
            </DataTemplate>
          </ItemsControl.ItemTemplate>
        </ItemsControl>
      </StackPanel>

      <!-- Tags: a second kind of ref, alongside branches rather than a tab of its own. -->
      <StackPanel Spacing="6">
        <Grid ColumnDefinitions="*,Auto">
          <TextBox Name="NewTagNameBox"
                   Text="{Binding NewTagName, Mode=TwoWay}"
                   Watermark="Name for a tag on the current commit" />
          <Button Grid.Column="1" Content="Tag this point"
                  Command="{Binding CreateTagCommand}" Margin="8,0,0,0" />
        </Grid>
        <TextBlock Text="Tags" FontWeight="Bold" />
        <ItemsControl Name="TagsHost" ItemsSource="{Binding Tags}">
          <ItemsControl.ItemTemplate>
            <DataTemplate x:DataType="vm:TagRowViewModel">
              <Grid ColumnDefinitions="*,Auto" Margin="0,3">
                <StackPanel Spacing="2">
                  <TextBlock Text="{Binding Name}" FontWeight="SemiBold" />
                  <TextBlock Text="{Binding TargetLabel}" Opacity="0.6" FontSize="12" />
                </StackPanel>
                <Button Grid.Column="1" Content="Delete" Command="{Binding DeleteCommand}" />
              </Grid>
            </DataTemplate>
          </ItemsControl.ItemTemplate>
        </ItemsControl>
      </StackPanel>

    </StackPanel>
  </ScrollViewer>
</UserControl>
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/GitHelper.App.Tests --filter "FullyQualifiedName~TabViewTests"`
Expected: PASS (all tests in the class).

- [ ] **Step 5: Commit**

```bash
git add src/GitHelper.App/Views/BranchesView.axaml tests/GitHelper.App.Tests/TabViewTests.cs
git commit -m "feat: show a Tags section on the Branches tab"
```

---

### Task 15: `StashRowViewModel` and the Stash section of `ChangesViewModel`

**Files:**
- Create: `src/GitHelper.App/ViewModels/StashRowViewModel.cs`
- Modify: `src/GitHelper.App/ViewModels/ChangesViewModel.cs`
- Create: `tests/GitHelper.App.Tests/ChangesStashTests.cs`

**Interfaces:**
- Consumes: `RepoState.Stashes`, `RepoState.HasUncommittedChanges`, action ids `stash` / `stash-pop` / `stash-apply` / `stash-drop` from earlier tasks.
- Produces: `StashRowViewModel(StashInfo, Func<string,string,Task>)` with `Message`, `RefLabel`, `PopCommand`, `ApplyCommand`, `DropCommand`; `ChangesViewModel.Stashes` (`ObservableCollection<StashRowViewModel>`), `.StashMessage` (`string`, two-way bindable), `.CanStash` (`bool`), `.StashCommand`.

- [ ] **Step 1: Write the failing tests**

`tests/GitHelper.App.Tests/ChangesStashTests.cs` — new file, following the same fixture shape as `ChangesConnectRemoteTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/GitHelper.App.Tests --filter "FullyQualifiedName~ChangesStashTests"`
Expected: FAIL — `StashInfo`/`Stashes`/`StashMessage`/`CanStash`/`StashCommand` do not exist on `ChangesViewModel` yet (compile error).

- [ ] **Step 3: Create `StashRowViewModel`**

`src/GitHelper.App/ViewModels/StashRowViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.Input;
using GitHelper.Core.Model;

namespace GitHelper.App.ViewModels;

/// <summary>One stash entry in the Changes view.</summary>
public sealed class StashRowViewModel : ViewModelBase
{
    public StashRowViewModel(StashInfo stash, Func<string, string, Task> invokeAction)
    {
        Message = stash.Message;
        RefLabel = stash.Ref;

        PopCommand = new AsyncRelayCommand(() => invokeAction("stash-pop", stash.Ref));
        ApplyCommand = new AsyncRelayCommand(() => invokeAction("stash-apply", stash.Ref));
        DropCommand = new AsyncRelayCommand(() => invokeAction("stash-drop", stash.Ref));
    }

    public string Message { get; }

    public string RefLabel { get; }

    public IAsyncRelayCommand PopCommand { get; }

    public IAsyncRelayCommand ApplyCommand { get; }

    public IAsyncRelayCommand DropCommand { get; }
}
```

- [ ] **Step 4: Wire it into `ChangesViewModel`**

In `src/GitHelper.App/ViewModels/ChangesViewModel.cs`, change the constructor:

```csharp
    public ChangesViewModel(ExplainPanelViewModel explain, IBrowserLauncher? browser = null)
    {
        _explain = explain;
        _browser = browser;

        StageAllCommand = new AsyncRelayCommand(() => InvokeAsync("stage-all", path: null));
        UnstageAllCommand = new AsyncRelayCommand(() => InvokeAsync("unstage-all", path: null));
        CommitCommand = new AsyncRelayCommand(CommitAsync);
        PushCommand = new AsyncRelayCommand(() => InvokeAsync("push", path: null));
        CreateGitignoreCommand = new AsyncRelayCommand(CreateGitignoreAsync);
        ConnectRemoteCommand = new AsyncRelayCommand(ConnectRemoteAsync);
        OpenGitHubCommand = new RelayCommand(() => _browser?.Open(NewRepositoryUrl));
    }
```

to:

```csharp
    public ChangesViewModel(ExplainPanelViewModel explain, IBrowserLauncher? browser = null)
    {
        _explain = explain;
        _browser = browser;

        StageAllCommand = new AsyncRelayCommand(() => InvokeAsync("stage-all", path: null));
        UnstageAllCommand = new AsyncRelayCommand(() => InvokeAsync("unstage-all", path: null));
        CommitCommand = new AsyncRelayCommand(CommitAsync);
        PushCommand = new AsyncRelayCommand(() => InvokeAsync("push", path: null));
        CreateGitignoreCommand = new AsyncRelayCommand(CreateGitignoreAsync);
        ConnectRemoteCommand = new AsyncRelayCommand(ConnectRemoteAsync);
        OpenGitHubCommand = new RelayCommand(() => _browser?.Open(NewRepositoryUrl));
        StashCommand = new AsyncRelayCommand(StashAsync);
    }
```

Add the collection, after `public ObservableCollection<FileChangeRowViewModel> Unstaged { get; } = new();`:

```csharp
    public ObservableCollection<FileChangeRowViewModel> Unstaged { get; } = new();

    public ObservableCollection<StashRowViewModel> Stashes { get; } = new();
```

Add the observable properties, after `[ObservableProperty] private string _remoteUrl = string.Empty;`:

```csharp
    [ObservableProperty] private string _remoteUrl = string.Empty;
    [ObservableProperty] private string _stashMessage = string.Empty;
    [ObservableProperty] private bool _canStash;
```

Add the command property, after `public IRelayCommand OpenGitHubCommand { get; }`:

```csharp
    public IRelayCommand OpenGitHubCommand { get; }

    /// <summary>
    /// Safe, so it runs on click rather than waiting for an inline Confirm — reversible via
    /// its own undo action (stash-pop), the same tier as stage-all/unstage-all.
    /// </summary>
    public IAsyncRelayCommand StashCommand { get; }
```

Populate `Stashes` and `CanStash` in `Update`. Change:

```csharp
    public void Update(RepoState state, FolderState? folder)
    {
        _repoPath = state.RepoRoot;
        _folder = folder;

        Staged.Clear();
        foreach (var change in state.Staged)
            Staged.Add(new FileChangeRowViewModel(change, staged: true, InvokeWithPathAsync));

        Unstaged.Clear();
        // RepoState.Unstaged excludes untracked files by design; the view shows one
        // combined "not staged" list.
        foreach (var change in state.Unstaged.Concat(state.Untracked))
            Unstaged.Add(new FileChangeRowViewModel(change, staged: false, InvokeWithPathAsync));

        HasStagedChanges = Staged.Count > 0;
        HasAnyChanges = Staged.Count > 0 || Unstaged.Count > 0;

        UpdatePushPrompt(state);

        // Offered only when the folder is known and has none. A repository with a .gitignore
        // already curated by the user is none of the app's business.
        HasGitignoreOffer = folder is { HasGitignore: false };
    }
```

to:

```csharp
    public void Update(RepoState state, FolderState? folder)
    {
        _repoPath = state.RepoRoot;
        _folder = folder;

        Staged.Clear();
        foreach (var change in state.Staged)
            Staged.Add(new FileChangeRowViewModel(change, staged: true, InvokeWithPathAsync));

        Unstaged.Clear();
        // RepoState.Unstaged excludes untracked files by design; the view shows one
        // combined "not staged" list.
        foreach (var change in state.Unstaged.Concat(state.Untracked))
            Unstaged.Add(new FileChangeRowViewModel(change, staged: false, InvokeWithPathAsync));

        Stashes.Clear();
        foreach (var stash in state.Stashes)
            Stashes.Add(new StashRowViewModel(stash, InvokeWithStashAsync));

        HasStagedChanges = Staged.Count > 0;
        HasAnyChanges = Staged.Count > 0 || Unstaged.Count > 0;
        CanStash = state.HasUncommittedChanges;

        UpdatePushPrompt(state);

        // Offered only when the folder is known and has none. A repository with a .gitignore
        // already curated by the user is none of the app's business.
        HasGitignoreOffer = folder is { HasGitignore: false };
    }
```

Add the stash-clearing check to `OnActionCompleted`. Change:

```csharp
    public void OnActionCompleted(ActionOutcome outcome)
    {
        if (outcome.Success
            && outcome.After.RecentCommits.Count > outcome.Before.RecentCommits.Count)
        {
            CommitMessage = string.Empty;
        }

        // Driven by a remote observably appearing, not by which action was requested, so a
        // rejected address stays in the box for the user to correct.
        if (outcome.Success && !outcome.Before.HasRemote && outcome.After.HasRemote)
            RemoteUrl = string.Empty;
    }
```

to:

```csharp
    public void OnActionCompleted(ActionOutcome outcome)
    {
        if (outcome.Success
            && outcome.After.RecentCommits.Count > outcome.Before.RecentCommits.Count)
        {
            CommitMessage = string.Empty;
        }

        // Driven by a remote observably appearing, not by which action was requested, so a
        // rejected address stays in the box for the user to correct.
        if (outcome.Success && !outcome.Before.HasRemote && outcome.After.HasRemote)
            RemoteUrl = string.Empty;

        if (outcome.Success && outcome.After.Stashes.Count > outcome.Before.Stashes.Count)
            StashMessage = string.Empty;
    }
```

Finally, add the helper method and `StashAsync`, after `ConnectRemoteAsync`:

```csharp
    private Task ConnectRemoteAsync()
        => _repoPath is null
            ? Task.CompletedTask
            : _explain.ShowAndRunIfUngatedAsync(
                _repoPath, new ActionRequest("connect-remote", RemoteUrl: RemoteUrl));
```

to:

```csharp
    private Task ConnectRemoteAsync()
        => _repoPath is null
            ? Task.CompletedTask
            : _explain.ShowAndRunIfUngatedAsync(
                _repoPath, new ActionRequest("connect-remote", RemoteUrl: RemoteUrl));

    private Task InvokeWithStashAsync(string actionId, string stashRef)
        => _repoPath is null
            ? Task.CompletedTask
            : _explain.ShowAndRunIfUngatedAsync(
                _repoPath, new ActionRequest(actionId, StashRef: stashRef));

    private Task StashAsync()
        => _repoPath is null
            ? Task.CompletedTask
            : _explain.ShowAndRunIfUngatedAsync(
                _repoPath,
                new ActionRequest(
                    "stash",
                    Message: string.IsNullOrWhiteSpace(StashMessage) ? null : StashMessage));
```

- [ ] **Step 5: Run to verify everything passes**

Run: `dotnet test tests/GitHelper.App.Tests --filter "FullyQualifiedName~ChangesStashTests"`
Expected: PASS (all 5 tests).

Run: `dotnet test tests/GitHelper.App.Tests --filter "FullyQualifiedName~ChangesViewModelTests"`
Expected: PASS — confirms the `OnActionCompleted` edit didn't disturb the existing commit-message and remote-url clearing behaviour.

- [ ] **Step 6: Commit**

```bash
git add src/GitHelper.App/ViewModels/StashRowViewModel.cs src/GitHelper.App/ViewModels/ChangesViewModel.cs tests/GitHelper.App.Tests/ChangesStashTests.cs
git commit -m "feat: set aside, bring back, and delete stashes on the Changes tab"
```

---

### Task 16: Stash section in `ChangesView.axaml`

**Files:**
- Modify: `src/GitHelper.App/Views/ChangesView.axaml`
- Modify: `tests/GitHelper.App.Tests/TabViewTests.cs`

**Interfaces:**
- Consumes: `ChangesViewModel.Stashes`, `.StashMessage`, `.CanStash`, `.StashCommand`, `StashRowViewModel.Message`/`.RefLabel`/`.PopCommand`/`.ApplyCommand`/`.DropCommand` from Task 15.

- [ ] **Step 1: Write the failing test**

In `tests/GitHelper.App.Tests/TabViewTests.cs`, add after `ChangesView_BindsTheCommitBoxBothWays`:

```csharp
    [AvaloniaFact]
    public async Task ChangesView_ShowsStashRowsAndBindsTheStashMessageBox()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("README.md", "changed\n");
        await repo.GitAsync("stash", "push", "-m", "wip");
        var reader = new RepoStateReader(new GitRunner());
        var vm = new ChangesViewModel(NewPanel());
        vm.Update(await reader.ReadAsync(repo.Path), null);

        var view = new ChangesView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();

        Assert.NotNull(view.FindControl<ItemsControl>("StashesHost"));
        var box = view.FindControl<TextBox>("StashMessageBox");
        Assert.NotNull(box);
        Assert.Single(vm.Stashes);

        box!.Text = "next";
        Assert.Equal("next", vm.StashMessage);
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/GitHelper.App.Tests --filter "FullyQualifiedName~TabViewTests"`
Expected: FAIL — `StashesHost` and `StashMessageBox` are not in the view yet.

- [ ] **Step 3: Add the Stash section**

In `src/GitHelper.App/Views/ChangesView.axaml`, change:

```xml
        <!-- Not staged -->
        <StackPanel Spacing="6">
          <Grid ColumnDefinitions="*,Auto">
            <TextBlock Text="Changed, but not chosen yet" FontWeight="Bold" VerticalAlignment="Center" />
            <Button Grid.Column="1" Content="Stage all" Command="{Binding StageAllCommand}" />
          </Grid>
          <ItemsControl Name="UnstagedHost" ItemsSource="{Binding Unstaged}">
            <ItemsControl.ItemTemplate>
              <DataTemplate x:DataType="vm:FileChangeRowViewModel">
                <Grid ColumnDefinitions="*,Auto,Auto" Margin="0,3">
                  <StackPanel>
                    <TextBlock Text="{Binding Path}" TextTrimming="CharacterEllipsis" />
                    <TextBlock Text="{Binding StatusLabel}" Opacity="0.6" FontSize="12" />
                  </StackPanel>
                  <Button Grid.Column="1" Content="Stage" Command="{Binding StageCommand}" />
                  <Button Grid.Column="2" Content="Discard" Command="{Binding DiscardCommand}"
                          Margin="6,0,0,0" />
                </Grid>
              </DataTemplate>
            </ItemsControl.ItemTemplate>
          </ItemsControl>
        </StackPanel>

      </StackPanel>
    </ScrollViewer>
```

to:

```xml
        <!-- Not staged -->
        <StackPanel Spacing="6">
          <Grid ColumnDefinitions="*,Auto">
            <TextBlock Text="Changed, but not chosen yet" FontWeight="Bold" VerticalAlignment="Center" />
            <Button Grid.Column="1" Content="Stage all" Command="{Binding StageAllCommand}" />
          </Grid>
          <ItemsControl Name="UnstagedHost" ItemsSource="{Binding Unstaged}">
            <ItemsControl.ItemTemplate>
              <DataTemplate x:DataType="vm:FileChangeRowViewModel">
                <Grid ColumnDefinitions="*,Auto,Auto" Margin="0,3">
                  <StackPanel>
                    <TextBlock Text="{Binding Path}" TextTrimming="CharacterEllipsis" />
                    <TextBlock Text="{Binding StatusLabel}" Opacity="0.6" FontSize="12" />
                  </StackPanel>
                  <Button Grid.Column="1" Content="Stage" Command="{Binding StageCommand}" />
                  <Button Grid.Column="2" Content="Discard" Command="{Binding DiscardCommand}"
                          Margin="6,0,0,0" />
                </Grid>
              </DataTemplate>
            </ItemsControl.ItemTemplate>
          </ItemsControl>
        </StackPanel>

        <!-- Stash: a shelf for changes you are not ready to commit, folded into this tab
             rather than a tab of its own because it acts on the same changes shown above. -->
        <StackPanel Spacing="6">
          <Grid ColumnDefinitions="*,Auto">
            <TextBox Name="StashMessageBox"
                     Text="{Binding StashMessage, Mode=TwoWay}"
                     Watermark="Describe what you're setting aside (optional)" />
            <Button Grid.Column="1" Content="Set aside"
                    Command="{Binding StashCommand}" IsEnabled="{Binding CanStash}"
                    Margin="8,0,0,0" />
          </Grid>
          <TextBlock Text="Stashed" FontWeight="Bold" />
          <ItemsControl Name="StashesHost" ItemsSource="{Binding Stashes}">
            <ItemsControl.ItemTemplate>
              <DataTemplate x:DataType="vm:StashRowViewModel">
                <Grid ColumnDefinitions="*,Auto,Auto,Auto" Margin="0,3">
                  <TextBlock Text="{Binding Message}" TextTrimming="CharacterEllipsis" />
                  <Button Grid.Column="1" Content="Bring back" Command="{Binding PopCommand}" />
                  <Button Grid.Column="2" Content="Copy back" Command="{Binding ApplyCommand}"
                          Margin="6,0,0,0" />
                  <Button Grid.Column="3" Content="Delete" Command="{Binding DropCommand}"
                          Margin="6,0,0,0" />
                </Grid>
              </DataTemplate>
            </ItemsControl.ItemTemplate>
          </ItemsControl>
        </StackPanel>

      </StackPanel>
    </ScrollViewer>
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/GitHelper.App.Tests --filter "FullyQualifiedName~TabViewTests"`
Expected: PASS (all tests in the class).

- [ ] **Step 5: Commit**

```bash
git add src/GitHelper.App/Views/ChangesView.axaml tests/GitHelper.App.Tests/TabViewTests.cs
git commit -m "feat: show a Stash section on the Changes tab"
```

---

### Task 17: Update the roadmap, and a final full-suite check

**Files:**
- Modify: `docs/roadmap.md`

- [ ] **Step 1: Mark tags and stash as shipped**

In `docs/roadmap.md`, change:

```markdown
**Tags, stash, cherry-pick (clean), ~~remote management~~ (shipped).**
```

to:

```markdown
**~~Tags, stash~~ (shipped), cherry-pick (clean), ~~remote management~~ (shipped).**
```

Change the "Remote management shipped first" paragraph's closing sentence — after it, add a new paragraph following the same pattern:

```markdown
**Remote management shipped first**, as `connect-remote` and `disconnect-remote`, because it
was the half of repository setup the sibling spec left open. It confirmed the estimate above:
two descriptors, two content files, two glossary terms, and no new UI paradigm — but it also
needed a precondition to validate a pasted URL, which is the first time argv has carried a
value straight from the clipboard.
```

to:

```markdown
**Remote management shipped first**, as `connect-remote` and `disconnect-remote`, because it
was the half of repository setup the sibling spec left open. It confirmed the estimate above:
two descriptors, two content files, two glossary terms, and no new UI paradigm — but it also
needed a precondition to validate a pasted URL, which is the first time argv has carried a
value straight from the clipboard.

**Tags and stash shipped next**, closing out v1.1. Both confirmed the "budget a small tab, not
zero" caveat above: `RepoState` needed two new lists (`Tags`, `Stashes`), two new parsers, and a
new section embedded in an existing tab rather than a new one — Tags beside Branches, Stash
beside Changes. Stash also confirmed the "an action is atomic" assumption is worth defending
deliberately: `stash-pop` and `stash-apply` are only offered against a clean working tree, so
v1.1 never had to invent a shape for a conflicting pop.
```

Update the sequence table. Change:

```markdown
| **v1.1** | ~~Remote management~~ (shipped), tags, stash | No new concepts; proves the descriptor model scales past the original thirteen |
```

to:

```markdown
| **v1.1** | ~~Remote management, tags, stash~~ (all shipped) | No new concepts; proved the descriptor model scales past the original thirteen |
```

- [ ] **Step 2: Run the full suite one last time**

Run: `dotnet test GitHelper.sln`
Expected: PASS — every test in both `GitHelper.Core.Tests` and `GitHelper.App.Tests` is green.

- [ ] **Step 3: Commit**

```bash
git add docs/roadmap.md
git commit -m "docs: mark tags and stash shipped in the roadmap"
```

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-08-03-tags-and-stash.md`. Two execution options:

**1. Subagent-Driven (recommended)** — dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — execute tasks in this session using executing-plans, batch execution with checkpoints.

Which approach?
