# GitHub Publishing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a beginner connect a local repository to an empty GitHub repository they created themselves, and understand — at the moment it matters — that git and GitHub are different things.

**Architecture:** Two ordinary `GitAction` descriptors (`connect-remote`, `disconnect-remote`) plus two preconditions, one of which validates a clipboard-sourced URL before it can reach argv. No new operation kind: the `SetupService` machinery is not used. The Changes tab's existing push prompt grows a third state that offers the connect flow, and a new `IBrowserLauncher` seam opens `github.com/new` without the viewmodel touching Avalonia.

**Tech Stack:** .NET 10, Avalonia 11.3, CommunityToolkit.Mvvm 8.4, xUnit.

**Spec:** [docs/superpowers/specs/2026-07-28-github-publishing-design.md](../specs/2026-07-28-github-publishing-design.md)

## Global Constraints

- **No credentials, ever.** No view may contain a password or token field, and the app never collects, stores, or transmits one. The first push signs in through git's own credential helper.
- **argv only.** Every git invocation is a `string[]` through `IGitRunner`. Never a joined string, never a shell, never stdin.
- **Argument injection is a real risk here.** The remote URL is pasted by the user and lands in argv. A value beginning with `-` is read by git as a flag. Validation happens in a precondition, before `BuildArgs` is ever reached.
- **`origin` is the only remote this app manages.** No second remote, no rename, no re-point — disconnect and reconnect covers it.
- **`GitHelper.Core` has no Avalonia reference.** Views may touch Avalonia and engine types; viewmodels may not touch Avalonia.
- **The content id equals the action id**, by convention, and `ContentIntegrityTests` enforces it. A new action with no content file fails the build's test run, as does a glossary term nothing references.
- **Warnings are errors** (`TreatWarningsAsErrors`) in every project.

**Run all tests with:**

```bash
dotnet test
```

---

### Task 1: The remote URL and its two preconditions

**Files:**
- Modify: `src/GitHelper.Core/Actions/ActionRequest.cs`
- Modify: `src/GitHelper.Core/Actions/Preconditions.cs`
- Test: `tests/GitHelper.Core.Tests/PreconditionTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `ActionRequest(string ActionId, string? Path = null, string? Message = null, string? BranchName = null, string? RemoteUrl = null)`
  - `sealed class RequiresNoRemote : IPrecondition`
  - `sealed class RequiresValidRemoteUrl : IPrecondition`

**Why validation is a precondition rather than a check inside `BuildArgs`.** `ActionService.PreviewAsync` only calls `BuildArgs` once every precondition passes, so a rejected URL never reaches argv at all — and the rejection arrives as a readable blocker in the explain panel, which is where every other "you cannot do this yet" message already appears.

- [ ] **Step 1: Write the failing tests**

Add to `tests/GitHelper.Core.Tests/PreconditionTests.cs`, at the end of the class:

```csharp
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
        // No space in the input: whitespace is rejected by its own rule, with its own
        // message, one check earlier.
        var result = new RequiresValidRemoteUrl().Evaluate(State(hasRemote: false), UrlRequest("my-project"));

        Assert.False(result.Satisfied);
        Assert.Contains("https://", result.Message!, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:

```bash
dotnet test tests/GitHelper.Core.Tests --filter "FullyQualifiedName~PreconditionTests"
```

Expected: build failure — `RequiresNoRemote` and `RequiresValidRemoteUrl` do not exist, and `ActionRequest` has no `RemoteUrl`.

- [ ] **Step 3: Add `RemoteUrl` to the request**

Replace the record in `src/GitHelper.Core/Actions/ActionRequest.cs`:

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
    string? BranchName = null,
    string? RemoteUrl = null);
```

- [ ] **Step 4: Write the two preconditions**

Append to `src/GitHelper.Core/Actions/Preconditions.cs`:

```csharp
public sealed class RequiresNoRemote : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
        => state.HasRemote
            ? PreconditionResult.Fail(
                "This project already has an online copy configured. Connecting a second one "
                + "would leave two addresses to keep straight, so disconnect the current one "
                + "first if you want to point it somewhere else.",
                "disconnect-remote")
            : PreconditionResult.Ok;
}

/// <summary>
/// The one value in this app that arrives from the clipboard and ends up in argv. Argv
/// arrays prevent shell injection but not argument injection: git reads a leading '-' as a
/// flag, so `--upload-pack=...` pasted here would be an instruction rather than a place.
/// The messages are written for someone who pasted the wrong thing, not for an attacker.
/// </summary>
public sealed class RequiresValidRemoteUrl : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
    {
        var url = request.RemoteUrl?.Trim();

        if (string.IsNullOrEmpty(url))
            return PreconditionResult.Fail(
                "Paste the address of the empty repository you created on GitHub. GitHub shows "
                + "it on the page you land on straight after creating one.");

        if (url.StartsWith('-'))
            return PreconditionResult.Fail(
                "An address cannot start with a dash — git would read that as an instruction "
                + "rather than a place. Copy the address again from GitHub.");

        if (url.Any(char.IsWhiteSpace))
            return PreconditionResult.Fail(
                "That address has a space in it, so something else was probably copied along "
                + "with it. Copy just the address.");

        if (url.StartsWith("git@", StringComparison.Ordinal))
            return PreconditionResult.Ok;

        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return PreconditionResult.Fail(
                "A project address starts with https:// or git@. Copy it from the green Code "
                + "button on the project's page on GitHub.");

        return IsPageInsideTheProject(url)
            ? PreconditionResult.Fail(
                "That is the address of a page inside the project rather than the project "
                + "itself. Go to the project's front page and copy the address from the green "
                + "Code button.")
            : PreconditionResult.Ok;
    }

    /// <summary>
    /// A clone address is host, owner, project — nothing deeper. Anything longer is a page
    /// the user happened to be looking at: /tree/main, /settings, /pull/3.
    /// </summary>
    private static bool IsPageInsideTheProject(string url)
    {
        var afterScheme = url["https://".Length..].TrimEnd('/');
        return afterScheme.Split('/', StringSplitOptions.RemoveEmptyEntries).Length > 3;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run:

```bash
dotnet test tests/GitHelper.Core.Tests --filter "FullyQualifiedName~PreconditionTests"
```

Expected: PASS, all tests.

- [ ] **Step 6: Commit**

```bash
git add src/GitHelper.Core/Actions/ActionRequest.cs src/GitHelper.Core/Actions/Preconditions.cs tests/GitHelper.Core.Tests/PreconditionTests.cs
git commit -m "feat: validate a pasted remote address before it can reach argv"
```

---

### Task 2: The connect and disconnect actions, with their content

**Files:**
- Modify: `src/GitHelper.Core/Actions/ActionCatalog.cs`
- Modify: `src/GitHelper.Core/Content/SlotBinder.cs`
- Modify: `src/GitHelper.Core/Actions/ActionService.cs:27`
- Create: `src/GitHelper.Content/actions/connect-remote.md`
- Create: `src/GitHelper.Content/actions/disconnect-remote.md`
- Create: `src/GitHelper.Content/terms/origin.md`
- Create: `src/GitHelper.Content/terms/github.md`
- Test: `tests/GitHelper.Core.Tests/ActionCatalogTests.cs`
- Test: `tests/GitHelper.Core.Tests/SlotBinderTests.cs`

**Interfaces:**
- Consumes: `ActionRequest.RemoteUrl`, `RequiresNoRemote`, `RequiresValidRemoteUrl` from Task 1.
- Produces:
  - Catalog ids `"connect-remote"` (Caution, undo `disconnect-remote`) and `"disconnect-remote"` (Caution).
  - `SlotBinder.Bind(RepoState state, string? path = null, string? branchName = null, string? remoteUrl = null)` and a `"remoteUrl"` entry in `SlotBinder.KnownSlots`.

**Why the descriptors, the content, and the terms land in one task.** `ContentIntegrityTests` asserts both directions — every action has a content file, every glossary term is referenced by something. Adding a descriptor without its content, or a term without a reference, leaves the suite red. They are one deliverable.

- [ ] **Step 1: Write the failing tests**

In `tests/GitHelper.Core.Tests/ActionCatalogTests.cs`, replace `All_ContainsExactlyTheThirteenV1Actions` with:

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

Then append to the same class:

```csharp
    [Fact]
    public void ConnectRemote_BuildsRemoteAddOriginWithTheTrimmedUrl()
    {
        var action = ActionCatalog.Find("connect-remote")!;
        var state = DetachedFreeState();

        var args = action.BuildArgs(state, new ActionRequest(
            "connect-remote", RemoteUrl: "  https://github.com/me/project.git  "));

        Assert.Equal(
            new[] { "remote", "add", "origin", "https://github.com/me/project.git" }, args);
    }

    [Fact]
    public void DisconnectRemote_BuildsRemoteRemoveOrigin()
    {
        var action = ActionCatalog.Find("disconnect-remote")!;

        var args = action.BuildArgs(DetachedFreeState(), new ActionRequest("disconnect-remote"));

        Assert.Equal(new[] { "remote", "remove", "origin" }, args);
    }

    [Fact]
    public void ConnectRemote_UndoesToDisconnectRemote()
    {
        Assert.Equal("disconnect-remote", ActionCatalog.Find("connect-remote")!.UndoActionId);
    }

    /// <summary>A minimal state for descriptors that read nothing out of it.</summary>
    private static RepoState DetachedFreeState() => new(
        RepoRoot: @"C:\r", Branch: "main", IsDetached: false, Upstream: null,
        Ahead: 0, Behind: 0, HasCommits: true, HasRemote: false,
        Changes: Array.Empty<FileChange>(),
        RecentCommits: Array.Empty<CommitInfo>(),
        Branches: Array.Empty<BranchInfo>());
```

Append to `tests/GitHelper.Core.Tests/SlotBinderTests.cs`:

```csharp
    [Fact]
    public void RemoteUrlIsBoundWhenSuppliedAndDescribedWhenNot()
    {
        Assert.Equal(
            "https://github.com/me/p.git",
            SlotBinder.Bind(State(), remoteUrl: "https://github.com/me/p.git")["remoteUrl"]);

        Assert.Equal("the address you paste", SlotBinder.Bind(State())["remoteUrl"]);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:

```bash
dotnet test tests/GitHelper.Core.Tests --filter "FullyQualifiedName~ActionCatalogTests|FullyQualifiedName~SlotBinderTests"
```

Expected: build failure — `Bind` has no `remoteUrl` parameter, and `ActionCatalog.Find("connect-remote")` returns null.

- [ ] **Step 3: Add the `remoteUrl` slot**

In `src/GitHelper.Core/Content/SlotBinder.cs`, add `"remoteUrl"` to the `KnownSlots` set and extend `Bind`:

```csharp
    public static IReadOnlySet<string> KnownSlots { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "branch", "upstream", "ahead", "behind",
        "stagedCount", "unstagedCount", "untrackedCount",
        "stagedFileList", "path", "branchName", "repoName", "remoteUrl",
    };

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

In `src/GitHelper.Core/Actions/ActionService.cs:27`, pass it through:

```csharp
        var slots = SlotBinder.Bind(state, request.Path, request.BranchName, request.RemoteUrl);
```

- [ ] **Step 4: Add the two descriptors**

In `src/GitHelper.Core/Actions/ActionCatalog.cs`, insert after the `delete-branch` entry (keeping the trailing comma structure intact):

```csharp
        new GitAction(
            Id: "connect-remote",
            Title: "Connect to GitHub",
            Danger: Danger.Caution,
            // Trimmed here as well as in the precondition: the two must agree on the exact
            // string, and the user's paste routinely carries trailing whitespace.
            BuildArgs: (_, r) => new[] { "remote", "add", "origin", r.RemoteUrl!.Trim() },
            Preconditions: new IPrecondition[]
            {
                new RequiresNoRemote(), new RequiresValidRemoteUrl(),
            },
            UndoActionId: "disconnect-remote"),

        new GitAction(
            Id: "disconnect-remote",
            Title: "Disconnect from GitHub",
            Danger: Danger.Caution,
            BuildArgs: (_, _) => new[] { "remote", "remove", "origin" },
            Preconditions: new IPrecondition[] { new RequiresRemote() }),
```

- [ ] **Step 5: Write the content for connect-remote**

Create `src/GitHelper.Content/actions/connect-remote.md`:

```markdown
---
id: connect-remote
title: Connect to GitHub
danger: caution
terms: [remote, origin, github, local-repository]
undo: disconnect-remote
---
## what
Records where this project's online copy lives. Git files the address {remoteUrl} under the
nickname [[origin]], so that sending and getting changes later know where to go.

Nothing is uploaded by this step. It writes an address into this project's settings, and
that is all. Your [[local-repository|project on this computer]] and the copy on
[[github|GitHub]] stay two separate things until you send your work.

That separation is worth knowing: git is the program on your computer that tracks changes,
and GitHub is a company that stores copies of projects. Git works perfectly well without
GitHub. Connecting the two is what gets your work backed up and visible to other people.

## risks
The repository you created on GitHub must be **empty** — no README, no .gitignore, no
licence. If GitHub added any of those, it already has a history of its own, and your first
send will be refused because the two histories have nothing in common.

A wrong address is not noticed here. Git accepts any address that looks like one, and the
mistake only surfaces when you try to send.

Signing in happens the first time you send, not now. Git handles that itself and may open a
browser window for it. This app never sees, asks for, or stores your password.

## undo
Disconnecting removes the address again. Nothing already sent is affected, and your commits
stay exactly where they are — an address is the only thing this wrote.
```

- [ ] **Step 6: Write the content for disconnect-remote**

Create `src/GitHelper.Content/actions/disconnect-remote.md`:

```markdown
---
id: disconnect-remote
title: Disconnect from GitHub
danger: caution
terms: [remote, origin, upstream]
---
## what
Forgets the address of this project's online copy. The nickname [[origin]] stops pointing
anywhere, and sending or getting changes is unavailable until an address is set again.

## risks
Your commits are untouched: this changes an address, not history. Anything already sent
stays on the server, and this app cannot delete it from there.

If you disconnect while work exists only on this computer, that work has no backup until you
connect somewhere and send again.

## undo
Connect again with the same address. Removing the [[remote|online copy]] also removes the
[[upstream]] link between your branch and the branch on the server, so the first send
afterwards sets that link up again — exactly as it did the first time.
```

- [ ] **Step 7: Write the two glossary terms**

Create `src/GitHelper.Content/terms/origin.md`:

```markdown
---
id: origin
title: origin
---
## definition
The nickname git gives the first online copy you connect to. There is nothing official about
the word — it is a label, and it could be renamed — but nearly every project uses it, so it
is worth recognising when you see it.
```

Create `src/GitHelper.Content/terms/github.md`:

```markdown
---
id: github
title: GitHub
---
## definition
A company that stores copies of git projects online, so they are backed up and other people
can see them. Git and GitHub are not the same thing: git runs on your computer and works
without an account anywhere.
```

- [ ] **Step 8: Run the tests to verify they pass**

Run:

```bash
dotnet test tests/GitHelper.Core.Tests
```

Expected: PASS. `ContentIntegrityTests` is the one to watch — it checks that both new actions have content, that `danger` and `undo` in the frontmatter match the descriptors, that `origin` and `github` resolve, and that both new terms are referenced.

- [ ] **Step 9: Commit**

```bash
git add src/GitHelper.Core/Actions/ActionCatalog.cs src/GitHelper.Core/Content/SlotBinder.cs src/GitHelper.Core/Actions/ActionService.cs src/GitHelper.Content/actions/connect-remote.md src/GitHelper.Content/actions/disconnect-remote.md src/GitHelper.Content/terms/origin.md src/GitHelper.Content/terms/github.md tests/GitHelper.Core.Tests/ActionCatalogTests.cs tests/GitHelper.Core.Tests/SlotBinderTests.cs
git commit -m "feat: add connect-remote and disconnect-remote, with the content that teaches them"
```

---

### Task 3: First-push failures, and warning about the sign-in

**Files:**
- Modify: `src/GitHelper.Core/Errors/ErrorTranslator.cs`
- Modify: `src/GitHelper.Content/actions/push.md`
- Test: `tests/GitHelper.Core.Tests/ErrorTranslatorTests.cs`

**Interfaces:**
- Consumes: `disconnect-remote` exists (Task 2), so the copy can point at it by name.
- Produces: no new types. Two new translator rules, one reworded rule, one reworded content section.

**Why `(fetch first)` gets its own rule ahead of `non-fast-forward`.** Git reports `(fetch first)` whenever the server has a commit this copy has never seen — that covers both a collaborator having pushed already and, the case this feature creates, a brand-new repository the user let GitHub seed with a README. Git only reports `(non-fast-forward)` once the branch has been fetched and the two histories have diverged. Since one string covers two causes, the rule names both rather than guessing which one happened. Rules are ordered and first match wins, so the specific one goes first.

- [ ] **Step 1: Write the failing tests**

Add to `tests/GitHelper.Core.Tests/ErrorTranslatorTests.cs`:

```csharp
    [Fact]
    public void Translate_NamesTheReadmeTrapWhenAFirstSendIsRejected()
    {
        // Real git output when the GitHub repository was created with "Add a README" ticked.
        var translated = ErrorTranslator.Translate(Failure(
            " ! [rejected]        main -> main (fetch first)\n"
            + "error: failed to push some refs to 'https://github.com/me/project.git'"))!;

        Assert.True(translated.IsUnderstood);
        Assert.Contains(
            "README",
            translated.Summary + " " + translated.Explanation,
            StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(translated.NextSteps);
    }

    [Fact]
    public void Translate_AlsoOffersFetchingWhenSomeoneElsePushedFirst()
    {
        // The same "(fetch first)" git produces when a collaborator pushed and this copy
        // has not fetched yet — the advice must work for that case too, not only the
        // freshly created repository.
        var translated = ErrorTranslator.Translate(Failure(
            " ! [rejected]        main -> main (fetch first)\n"
            + "error: failed to push some refs to 'https://github.com/team/project.git'"))!;

        Assert.Contains(
            translated.NextSteps,
            step => step.Contains("Get the changes from the server first", StringComparison.Ordinal));
    }

    [Fact]
    public void Translate_StillBlamesTheOtherPersonForAnOrdinaryNonFastForward()
    {
        // The general rule must survive the more specific one being added ahead of it.
        var translated = ErrorTranslator.Translate(Failure(
            " ! [rejected]        main -> main (non-fast-forward)"))!;

        Assert.Contains("someone else", translated.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Translate_SaysTheAddressCanBeChangedWhenTheRepositoryIsNotThere()
    {
        var translated = ErrorTranslator.Translate(Failure(
            "remote: Repository not found.\n"
            + "fatal: repository 'https://github.com/me/typo.git/' not found"))!;

        Assert.True(translated.IsUnderstood);
        Assert.Contains(
            translated.NextSteps,
            step => step.Contains("disconnect", StringComparison.OrdinalIgnoreCase));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:

```bash
dotnet test tests/GitHelper.Core.Tests --filter "FullyQualifiedName~ErrorTranslatorTests"
```

Expected: three failures. The first two report the generic non-fast-forward wording; the third reports `IsUnderstood` false.

- [ ] **Step 3: Add the two rules and reword the third**

In `src/GitHelper.Core/Errors/ErrorTranslator.cs`, make the `(fetch first)` rule the **first** entry in `Rules`, ahead of the existing `non-fast-forward` one:

```csharp
        // Ahead of "non-fast-forward" deliberately: git reports "(fetch first)" when the
        // server has a commit this copy has never seen, and "(non-fast-forward)" only once
        // it has been fetched and the two have diverged. Both causes below produce the
        // former, so the copy names both rather than guessing.
        new("(fetch first)",
            "The server has work your copy has not seen",
            "Your send was refused because the copy on the server has a commit yours knows "
            + "nothing about. Either someone else pushed and you have not fetched yet, or — if "
            + "this was your first send — the repository was created with a README, a .gitignore, "
            + "or a licence, which GitHub commits for you.",
            new[]
            {
                "Get the changes from the server first, then send yours again.",
                "If this was your first send, the repository was created with files in it: make a "
                + "new one with every 'add a file' option unticked, disconnect, and connect to that.",
            }),
```

Then add, immediately before the existing `"does not appear to be a git repository"` rule:

```csharp
        new("repository not found",
            "There is no project at that address",
            "Git reached the server, but found nothing at the address this project is "
            + "connected to. Either the address has a typo in it, or the repository is "
            + "private and this computer has not been given access.",
            new[]
            {
                "Check the address against the one GitHub shows on the project's page.",
                "Disconnect from GitHub and connect again with the corrected address.",
            }),
```

Finally, replace the `NextSteps` of the existing `"does not appear to be a git repository"` rule so it names the way out:

```csharp
        new("does not appear to be a git repository",
            "The server address does not work",
            "Git could not find a project at the address configured for this remote.",
            new[]
            {
                "Check the address against the project's page on GitHub.",
                "Disconnect from GitHub and connect again with the corrected address.",
            }),
```

- [ ] **Step 4: Warn about the sign-in in the push content**

In `src/GitHelper.Content/actions/push.md`, replace the `## risks` section with:

```markdown
## risks
What you send becomes visible to everyone with access to the project, so it is worth a
look at what is in your commits first.

The first time you send, git needs to prove who you are, and it may open a browser window
to do it. That window is git's own — this app never asks for, sees, or stores your password.

If someone else has pushed work you do not have, git will refuse. Get their changes
first, then send yours.
```

- [ ] **Step 5: Run the tests to verify they pass**

Run:

```bash
dotnet test tests/GitHelper.Core.Tests
```

Expected: PASS, including the pre-existing `Translate_RecognisesKnownFailures` theory rows.

- [ ] **Step 6: Commit**

```bash
git add src/GitHelper.Core/Errors/ErrorTranslator.cs src/GitHelper.Content/actions/push.md tests/GitHelper.Core.Tests/ErrorTranslatorTests.cs
git commit -m "feat: explain a rejected first push, and announce the sign-in before it happens"
```

---

### Task 4: The browser seam

**Files:**
- Create: `src/GitHelper.App/Infrastructure/IBrowserLauncher.cs`
- Create: `src/GitHelper.App/Infrastructure/ShellBrowserLauncher.cs`
- Modify: `tests/GitHelper.App.Tests/TestDoubles.cs`
- Test: `tests/GitHelper.App.Tests/UiAbstractionTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `interface IBrowserLauncher { void Open(string url); }` in `GitHelper.App.Infrastructure`
  - `sealed class ShellBrowserLauncher : IBrowserLauncher`
  - `sealed class StubBrowserLauncher : IBrowserLauncher` in the test project, exposing `LastUrl` and `CallCount`

**Why a seam for one `Process.Start`.** Mirrors `IFolderPicker`: it keeps the viewmodel free of platform calls, and it means a test can assert *which URL* is opened without a browser appearing on the machine running the suite.

- [ ] **Step 1: Write the failing test**

Add to `tests/GitHelper.App.Tests/UiAbstractionTests.cs`:

```csharp
public class StubBrowserLauncherTests
{
    [Fact]
    public void RecordsTheUrlItWasAskedToOpen()
    {
        var browser = new StubBrowserLauncher();

        browser.Open("https://github.com/new");

        Assert.Equal("https://github.com/new", browser.LastUrl);
        Assert.Equal(1, browser.CallCount);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```bash
dotnet test tests/GitHelper.App.Tests --filter "FullyQualifiedName~StubBrowserLauncherTests"
```

Expected: build failure — `StubBrowserLauncher` does not exist.

- [ ] **Step 3: Write the interface and both implementations**

Create `src/GitHelper.App/Infrastructure/IBrowserLauncher.cs`:

```csharp
namespace GitHelper.App.Infrastructure;

/// <summary>
/// Opens a URL in whatever browser the user has. A seam, mirroring IFolderPicker, so that
/// viewmodels stay free of platform calls and a test can assert the address without a
/// browser window appearing.
/// </summary>
public interface IBrowserLauncher
{
    void Open(string url);
}
```

Create `src/GitHelper.App/Infrastructure/ShellBrowserLauncher.cs`:

```csharp
using System.Diagnostics;

namespace GitHelper.App.Infrastructure;

/// <summary>
/// The real launcher. UseShellExecute hands the address to the operating system's default
/// handler rather than trying to locate a browser executable.
/// </summary>
public sealed class ShellBrowserLauncher : IBrowserLauncher
{
    public void Open(string url)
    {
        // Failing to open a browser must never take the app down with it: the user can
        // always reach github.com themselves, and the connect box is still usable.
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }
}
```

Append to `tests/GitHelper.App.Tests/TestDoubles.cs`:

```csharp
public sealed class StubBrowserLauncher : IBrowserLauncher
{
    public string? LastUrl { get; private set; }

    public int CallCount { get; private set; }

    public void Open(string url)
    {
        CallCount++;
        LastUrl = url;
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run:

```bash
dotnet test tests/GitHelper.App.Tests --filter "FullyQualifiedName~StubBrowserLauncherTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/GitHelper.App/Infrastructure/IBrowserLauncher.cs src/GitHelper.App/Infrastructure/ShellBrowserLauncher.cs tests/GitHelper.App.Tests/TestDoubles.cs tests/GitHelper.App.Tests/UiAbstractionTests.cs
git commit -m "feat: add a testable seam for opening a URL in the browser"
```

---

### Task 5: The "not on GitHub yet" offer on the Changes tab

**Files:**
- Modify: `src/GitHelper.App/ViewModels/ChangesViewModel.cs`
- Modify: `src/GitHelper.App/Views/ChangesView.axaml`
- Modify: `src/GitHelper.App/App.axaml.cs:60-71`
- Create: `tests/GitHelper.App.Tests/ChangesConnectRemoteTests.cs`
- Test: `tests/GitHelper.App.Tests/TabViewTests.cs`

**Interfaces:**
- Consumes: `connect-remote` from Task 2; `IBrowserLauncher` and `StubBrowserLauncher` from Task 4.
- Produces, on `ChangesViewModel`:
  - `const string NewRepositoryUrl = "https://github.com/new"`
  - `bool HasNoRemoteOffer`, `string RemoteUrl` (two-way bound)
  - `IAsyncRelayCommand ConnectRemoteCommand`, `IRelayCommand OpenGitHubCommand`
  - constructor `ChangesViewModel(ExplainPanelViewModel explain, IBrowserLauncher? browser = null)`

**Why the launcher is an optional trailing parameter.** `ExplainPanelViewModel` already takes its `SetupService` this way, and nine existing construction sites do not care about a browser. The composition root supplies the real one; a viewmodel built without it simply has a `OpenGitHubCommand` that does nothing.

**Why this is a third state rather than a fourth banner.** The spec puts it in the unpushed-work prompt: with no remote, `UpdatePushPrompt` already suppresses the send prompt, so the two are mutually exclusive by construction and only ever one shows.

- [ ] **Step 1: Write the failing tests**

Create `tests/GitHelper.App.Tests/ChangesConnectRemoteTests.cs`:

```csharp
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
}
```

Add to `tests/GitHelper.App.Tests/TabViewTests.cs`:

```csharp
    [AvaloniaFact]
    public void ChangesView_ShowsTheConnectBoxWhenThereIsNoRemote()
    {
        var vm = new ChangesViewModel(NewPanel(), new StubBrowserLauncher());
        vm.Update(
            new GitHelper.Core.Model.RepoState(
                RepoRoot: @"C:\r", Branch: "main", IsDetached: false, Upstream: null,
                Ahead: 0, Behind: 0, HasCommits: true, HasRemote: false,
                Changes: Array.Empty<GitHelper.Core.Model.FileChange>(),
                RecentCommits: Array.Empty<GitHelper.Core.Model.CommitInfo>(),
                Branches: Array.Empty<GitHelper.Core.Model.BranchInfo>()),
            null);

        var view = new ChangesView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();

        Assert.NotNull(view.FindControl<TextBox>("RemoteUrlBox"));
        Assert.True(vm.HasNoRemoteOffer);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:

```bash
dotnet test tests/GitHelper.App.Tests --filter "FullyQualifiedName~ChangesConnectRemoteTests|FullyQualifiedName~TabViewTests"
```

Expected: build failure — `ChangesViewModel` has no second constructor parameter, no `HasNoRemoteOffer`, no `RemoteUrl`, and no commands.

- [ ] **Step 3: Extend the viewmodel**

In `src/GitHelper.App/ViewModels/ChangesViewModel.cs`, add the `using` for the infrastructure namespace, then apply these changes.

Field, constant, and constructor:

```csharp
public sealed partial class ChangesViewModel : ViewModelBase
{
    /// <summary>
    /// GitHub's create-a-repository page. A constant, never anything the user typed: the
    /// only address this app ever opens is this one.
    /// </summary>
    public const string NewRepositoryUrl = "https://github.com/new";

    private readonly ExplainPanelViewModel _explain;
    private readonly IBrowserLauncher? _browser;
    private string? _repoPath;
    private FolderState? _folder;

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

New observable properties, beside the existing ones:

```csharp
    [ObservableProperty] private bool _hasNoRemoteOffer;
    [ObservableProperty] private string _remoteUrl = string.Empty;
```

New commands, beside the existing ones:

```csharp
    /// <summary>
    /// Previews `git remote add origin <url>`. Caution, so it waits for the panel's inline
    /// Confirm rather than running on click.
    /// </summary>
    public IAsyncRelayCommand ConnectRemoteCommand { get; }

    /// <summary>
    /// Opens github.com/new so the user can create the empty repository themselves. The app
    /// stops here on purpose: creating it for them would need a token, and this app has no
    /// field for one.
    /// </summary>
    public IRelayCommand OpenGitHubCommand { get; }
```

In `UpdatePushPrompt`, set the new flag as the first thing it does:

```csharp
    private void UpdatePushPrompt(RepoState state)
    {
        // The third state, and the one a new project starts in: there is no online copy at
        // all. Mutually exclusive with the send prompt below, which suppresses itself
        // whenever HasRemote is false.
        HasNoRemoteOffer = !state.HasRemote;

        // Every precondition on the push action must already hold. Offering the button
        // otherwise would show a beginner a control whose only possible outcome is a
        // blocked-action message.
        if (!state.HasRemote || !state.HasCommits || state.IsDetached)
        {
            HasUnpushedCommits = false;
            UnpushedSummary = string.Empty;
            return;
        }
```

Extend `OnActionCompleted` so the address box clears the same way the commit box does:

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

And the command body, beside `CommitAsync`:

```csharp
    private Task ConnectRemoteAsync()
        => _repoPath is null
            ? Task.CompletedTask
            : _explain.ShowAndRunIfUngatedAsync(
                _repoPath, new ActionRequest("connect-remote", RemoteUrl: RemoteUrl));
```

- [ ] **Step 4: Add the banner to the view**

In `src/GitHelper.App/Views/ChangesView.axaml`, insert this `Border` immediately before the existing `HasUnpushedCommits` banner:

```xml
        <!-- The third state of the send prompt: there is no online copy at all. The app
             wires up the address; creating the repository stays the user's job, because
             doing it for them would need a token this app refuses to hold. -->
        <Border IsVisible="{Binding HasNoRemoteOffer}"
                Background="#20808080" CornerRadius="4" Padding="8">
          <StackPanel Spacing="6">
            <TextBlock Text="Not on GitHub yet" FontWeight="SemiBold" TextWrapping="Wrap" />
            <TextBlock Opacity="0.7" FontSize="12" TextWrapping="Wrap"
                       Text="This project only exists on this computer. Create an empty repository on GitHub — no README, no .gitignore — then paste its address here." />
            <Grid ColumnDefinitions="*,Auto,Auto">
              <TextBox Name="RemoteUrlBox"
                       Text="{Binding RemoteUrl, Mode=TwoWay}"
                       Watermark="https://github.com/you/your-project.git" />
              <Button Grid.Column="1" Content="Create on GitHub"
                      Command="{Binding OpenGitHubCommand}" Margin="6,0,0,0" />
              <Button Grid.Column="2" Content="Connect"
                      Command="{Binding ConnectRemoteCommand}" Margin="6,0,0,0" />
            </Grid>
          </StackPanel>
        </Border>
```

- [ ] **Step 5: Wire the real launcher into the composition root**

In `src/GitHelper.App/App.axaml.cs`, add the launcher beside the other infrastructure (after the `confirmations` line):

```csharp
        var browser = new ShellBrowserLauncher();
```

and pass it when the tab is constructed:

```csharp
            new ChangesViewModel(explain, browser),
```

- [ ] **Step 6: Run the tests to verify they pass**

Run:

```bash
dotnet test tests/GitHelper.App.Tests
```

Expected: PASS, including the existing `ChangesPushPromptTests` — those build `ChangesViewModel` with one argument, which still compiles.

- [ ] **Step 7: Commit**

```bash
git add src/GitHelper.App/ViewModels/ChangesViewModel.cs src/GitHelper.App/Views/ChangesView.axaml src/GitHelper.App/App.axaml.cs tests/GitHelper.App.Tests/ChangesConnectRemoteTests.cs tests/GitHelper.App.Tests/TabViewTests.cs
git commit -m "feat: offer to connect a project to GitHub from the Changes tab"
```

---

### Task 6: The journey, end to end

**Files:**
- Create: `tests/GitHelper.App.Tests/PublishJourneyTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–5.
- Produces: no production types. One test that walks the flow a user walks.

**Why it stops short of a push.** A real push needs a network and a credential helper, neither of which belongs in a test run. Everything up to the push is local, observable, and worth locking down: the offer appears, the address is validated, `origin` ends up set, and the branch is untouched.

- [ ] **Step 1: Write the failing test**

Create `tests/GitHelper.App.Tests/PublishJourneyTests.cs`:

```csharp
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
    [Fact]
    public async Task ARepositoryWithNoRemoteIsOfferedTheConnectFlowAndEndsUpWithOrigin()
    {
        using var repo = await TestRepo.CreateAsync();
        var runner = new GitRunner();
        var reader = new RepoStateReader(runner);
        var panel = new ExplainPanelViewModel(
            new ActionService(runner, reader, ContentLibrary.Load()),
            new StubConfirmationDialog(),
            new InMemorySettingsStore());
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
}
```

- [ ] **Step 2: Run the test to verify it passes**

Run:

```bash
dotnet test tests/GitHelper.App.Tests --filter "FullyQualifiedName~PublishJourneyTests"
```

Expected: PASS. Unlike the earlier tasks this test is written last against finished code, so a failure here means one of Tasks 1–5 is wrong — fix that rather than the assertion.

- [ ] **Step 3: Run the whole suite**

Run:

```bash
dotnet test
```

Expected: PASS, every project.

- [ ] **Step 4: Commit**

```bash
git add tests/GitHelper.App.Tests/PublishJourneyTests.cs
git commit -m "test: walk the publish journey from no remote to origin set"
```

---

### Task 7: Documentation

**Files:**
- Modify: `README.md:69-80` (features), `README.md:117-135` (the action table), `README.md:222` (the "Deliberately not in v1" section)
- Modify: `docs/roadmap.md:136-142` (the sequence table)

**Interfaces:**
- Consumes: the finished feature.
- Produces: no code.

**Why the roadmap is edited rather than left alone.** It is a living document whose stated job is that a decision made once is not re-litigated. Shipping part of v1.1 without recording it invites exactly that.

- [ ] **Step 1: Add the feature to the README's feature list**

In `README.md`, after the `.gitignore` bullet (line 72), insert:

```markdown
- **Publishes to GitHub** — creates the connection, explains that git and GitHub are different things, and warns about the empty-repository trap before it bites. It never asks for a token: you create the repository, the app wires it up.
```

- [ ] **Step 2: Update the action table**

In `README.md`, change the sentence above the table from "Thirteen actions" to:

```markdown
Fifteen actions, covering roughly the 90% of beginner git that doesn't involve conflicts.
```

and add two rows to the table, after `Delete branch`:

```markdown
| Connect to GitHub | `git remote add origin <url>` | Caution |
| Disconnect from GitHub | `git remote remove origin` | Caution |
```

Then add a fourth bullet to the teaching-decisions list under the table:

```markdown
- **The app connects; it never creates the GitHub repository.** Creating one through the API
  needs a personal access token, and no view in this app may contain a token field. The
  trade is a click on github.com in exchange for a promise the app can keep absolutely.
```

- [ ] **Step 3: Add a section on publishing**

In `README.md`, immediately before `## The action set`, add:

```markdown
## Publishing to GitHub

Most beginners are never told that git and GitHub are two different things. The Changes tab
says so at the moment it matters — when a project has commits and no online copy:

> **Not on GitHub yet**
> This project only exists on this computer. Create an empty repository on GitHub — no
> README, no .gitignore — then paste its address here.
> `[ Create on GitHub ]`  `[ Connect ]`

The empty-repository warning is not decoration. A repository created with "Add a README"
ticked already has a commit of its own, and the first push is then rejected for reasons the
raw git message ("non-fast-forward") gives a beginner no way to decode. Both the risk and
the recovery are spelled out — before, in the explanation, and after, in the translated
error.

**The app stops at the address.** Creating the repository for you would need a personal
access token, and this app has no field to type one into, by design. Signing in for the
first push is git's own credential helper, which may open a browser window — the push
explanation says so in advance, so it arrives expected rather than alarming.
```

- [ ] **Step 4: Update the roadmap's sequence table**

In `docs/roadmap.md`, update the `Last updated` line to `2026-07-28` and change the v1.1 row of the sequence table to:

```markdown
| **v1.1** | ~~Remote management~~ (shipped), tags, stash | No new concepts; proves the descriptor model scales past the original thirteen |
```

Then, at the end of the "Bucket 1 — More of the same" section, add:

```markdown
**Remote management shipped first**, as `connect-remote` and `disconnect-remote`, because it
was the half of repository setup the sibling spec left open. It confirmed the estimate above:
two descriptors, two content files, two glossary terms, and no new UI paradigm — but it also
needed a precondition to validate a pasted URL, which is the first time argv has carried a
value straight from the clipboard.
```

- [ ] **Step 5: Verify the documented commands match the code**

Run:

```bash
dotnet test tests/GitHelper.Core.Tests --filter "FullyQualifiedName~ActionCatalogTests"
```

Expected: PASS — the catalog test lists exactly the fifteen ids the README now documents.

- [ ] **Step 6: Commit**

```bash
git add README.md docs/roadmap.md
git commit -m "docs: cover publishing to GitHub and record it against the roadmap"
```
