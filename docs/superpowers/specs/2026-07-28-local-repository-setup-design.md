# GitHelper — Creating a repository locally

**Date:** 2026-07-28
**Status:** Approved design, ready for implementation planning
**Sibling:** [GitHub publishing](2026-07-28-github-publishing-design.md) — the second half,
built after this one.

## Purpose

Close the gap before the app's current starting line. GitHelper today can only open a folder
that is *already* a git repository; picking anything else produces a dead-end error. A
beginner with a folder of work and no repository — which is how every project starts — cannot
use the app at all.

This adds two things, in the app's existing teaching style:

1. **Create a repository** in a folder that is not one yet.
2. **Offer a `.gitignore`**, chosen for the kind of project detected.

Publishing that repository to GitHub is deliberately a separate piece of work. This one is
useful on its own: a beginner who never touches GitHub still needs version history.

## Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Entry point | The existing "not a git project" error becomes an offer | Picking an empty folder *is* starting fresh, so one entry point covers both cases. No second "New project" button. |
| Flow shape | Ordinary actions, surfaced by folder state | The app is "one action, one explanation, one command". A wizard would be a second UI paradigm, and the four-heading structure does not fit a wizard step. |
| `.gitignore` coverage | Detection plus five curated, commented templates | The app has to *explain* the file line by line. The upstream github/gitignore corpus is long, uncommented, and unexplainable — coverage would be bought at the cost of the product's purpose. |
| Existing `.gitignore` | Never overwritten | Clobbering a user's file is not undoable by anything the app can show. |
| First commit | Never automatic | The app would make a commit the user did not ask for, contradicting explain-then-confirm. |

### Rejected alternatives

- **A dedicated setup wizard.** A second paradigm; the teaching structure does not fit it.
- **Bundling the full github/gitignore collection.** Unexplainable, and a third-party corpus to
  vendor and keep current.
- **Creating the folder as well as the repository ("New project").** One entry point already
  covers both cases. A second can be added later if the single one proves confusing.

## The journey

Each step is a state-driven offer. Nothing chains automatically.

1. **Folder is not a repository.** The startup screen says so and offers to fix it. The wording
   depends on what is there — "I found 12 files here" versus "this folder is empty, that's
   fine" — because those are different situations to a beginner even though the command is the
   same.

   → previews `git init -b main` in the normal explain panel.

2. **No `.gitignore`.** A banner on the Changes tab, in the same style as the unpushed-work
   prompt: *"Would you like me to help you set up a .gitignore?"* The template is shown before
   it is written.

3. **Stage and commit.** Unchanged.

## Teaching

One new glossary term: **`local repository`** — the `.git` folder on this machine, the thing
`init` creates. The point it has to land is that history now exists, and that it exists *here*
and nowhere else. The remote half of that idea belongs to the sibling spec.

## Architecture

### The problem `init` creates

`ActionService.PreviewAsync` calls `reader.ReadAsync(repoPath)` unconditionally. Before `init`
there is no repository to read, so `init` cannot travel the existing path.

Three options were considered:

| | Approach | Verdict |
|---|---|---|
| A | A small parallel `SetupService` over a new `FolderState` | **Chosen** |
| B | Add `IsRepository` to `RepoState`, nullable throughout | Rejected — touches all twelve preconditions and every consumer, to model a state with no branch, no commits and no upstream |
| C | Special-case `init` in the viewmodel, calling `GitRunner` directly | Rejected — no preview and no four headings, which is the entire product |

A is chosen because "before a repository exists" is a genuinely different domain, not a
`RepoState` full of nulls. It stays small: exactly two operations live there, and both are
delivered by this spec.

### Setup operations

| Operation | Why it is not an ordinary `GitAction` |
|---|---|
| `init-repository` | No repository exists yet, so there is no `RepoState` to evaluate against |
| `create-gitignore` | **Not a git command at all** — a file write |

`create-gitignore` is why `SetupPreview` carries **either** a `CommandLine` **or**
`FileContents`. The panel renders whichever is present: "The command" becomes "The file",
showing the commented template. No fake command is invented, and no heading is silently
dropped.

### Components

**`GitHelper.Core`**

- `FolderState( Path, IsRepository, FileCount, HasGitignore, ProjectType )`, produced by
  `FolderInspector`.
- `ProjectType` — `DotNet | Node | Python | Java | Generic`.
- `SetupService` — `PreviewAsync` / `RunAsync`, mirroring `ActionService`'s shape so the panel
  can treat both alike.
- `SetupPreview` — the same four explanation blocks, plus `CommandLine` or `FileContents`.

**`GitHelper.Content`**

- Five `.gitignore` templates as embedded resources, each short and commented.
- Content files for `init-repository` and `create-gitignore`.
- Glossary term `local-repository`.

**`GitHelper.App`**

- `StartupViewModel` — the dead-end error becomes a `FolderIsNotARepository` state carrying a
  `FolderState`.
- `ExplainPanelViewModel` gains `ShowSetupAsync`, so there remains exactly one panel.
- `ChangesViewModel` — the `.gitignore` offer banner.

### Data flow

```
pick folder → FindRepoRootAsync = null → FolderInspector
   → StartupState.FolderIsNotARepository
   → "Start tracking" → panel previews `git init -b main` → confirm → run
   → open normally, via the existing RepositoryOpenedAsync path

refresh → MainViewModel inspects the repo root → FolderState
        → ChangesViewModel.Update(state, folderState)
   → no .gitignore? → offer banner, template chosen by ProjectType
```

**Who runs the inspection after the repository exists.** The `.gitignore` banner needs
`HasGitignore` and `ProjectType`, which live on `FolderState`, not `RepoState` — but by then
the app is past the startup screen. `FolderInspector` reads any folder, including a repository
root, so `MainViewModel.RefreshAsync` performs the inspection alongside the state read and
passes both to `ChangesViewModel`. The viewmodel does no filesystem work itself, matching how
it already receives `RepoState` rather than reading it.

## Error handling

- **Never overwrite an existing `.gitignore`.** If one appears between the offer and the run,
  refuse and say so.
- **`init` failing on permissions** goes through the existing error translator.
- **A folder that becomes a repository between inspection and run** — for example the user ran
  `git init` in a terminal meanwhile — is caught by re-checking before the run, the same way
  `ActionService.RunAsync` re-validates its preconditions rather than trusting the preview.

## Testing

- `FolderInspector` detection, table-driven across the five project types.
- Every `ProjectType` maps to a shipped template; every template is non-empty and commented.
- An existing `.gitignore` is never overwritten.
- `init` against a temp directory produces a real repository, on branch `main`.
- `init` is refused in a folder that is already a repository.
- One journey test: empty directory → init → gitignore → stage → commit.

## Out of scope

- **Everything GitHub.** Connecting a remote, publishing, and the local-versus-remote
  explanation are the [sibling spec](2026-07-28-github-publishing-design.md).
- SSH key setup, `.gitattributes`, and choosing a licence.
- Cloning an existing remote repository — a sibling feature, not part of this one.
- Appending to an existing `.gitignore` rather than declining. Offering to merge entries into a
  file the user already curated is a different, more delicate operation.
