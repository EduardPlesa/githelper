# GitHelper — Creating a repository and publishing it to GitHub

**Date:** 2026-07-28
**Status:** Approved design, ready for implementation planning

## Purpose

Close the gap before the app's current starting line. GitHelper today can only open a folder
that is *already* a git repository; picking anything else produces a dead-end error. A
beginner with a folder of work and no repository — which is how every project starts — cannot
use the app at all.

This adds three things, in the app's existing teaching style:

1. **Create a repository** in a folder that is not one yet.
2. **Offer a `.gitignore`**, chosen for the kind of project detected.
3. **Publish to GitHub**, and explain what "local" and "remote" actually mean while doing it.

## Decisions

| Decision | Choice | Rationale |
|---|---|---|
| How far into GitHub | App wires up; the user creates the empty repo on github.com | Creating it for them needs a personal access token. The app's standing rule is that it never collects, stores, or transmits credentials, and no view may contain a token field. That rule is worth more than the convenience. |
| Flow shape | Ordinary actions, surfaced by repository state | The app is "one action, one explanation, one command". A wizard would be a second UI paradigm, and the four-heading structure does not fit a wizard step. |
| Entry point | The existing "not a git project" error becomes an offer | Picking an empty folder *is* starting fresh, so one entry point covers both cases. No second "New project" button. |
| `.gitignore` coverage | Detection plus five curated, commented templates | The app has to *explain* the file line by line. The upstream github/gitignore corpus is long, uncommented, and unexplainable — coverage would be bought at the cost of the product's purpose. |
| First-push credentials | Git's own credential helper, warned about in advance | Git Credential Manager opens its own browser sign-in. The app must say this is coming, or it looks like the credential prompt it promised never to build. |

### Rejected alternatives

- **Creating the GitHub repository via the API.** Requires a token field the specs forbid.
- **A dedicated publish wizard.** A second paradigm; the teaching structure does not fit it.
- **Bundling the full github/gitignore collection.** Unexplainable, and a third-party corpus to
  vendor and keep current.
- **Automatic first commit after init.** The app would make a commit the user never asked for,
  contradicting explain-then-confirm.

## The journey

Each step is a state-driven offer, not a wizard. Nothing chains automatically.

1. **Folder is not a repository.** The startup screen says so and offers to fix it. The wording
   depends on what is there — "I found 12 files here" versus "this folder is empty, that's
   fine" — because those are different situations to a beginner even though the command is the
   same.

   → previews `git init -b main` in the normal explain panel.

2. **No `.gitignore`.** A banner on the Changes tab, in the same style as the unpushed-work
   prompt: *"Would you like me to help you set up a .gitignore?"* The template is shown before
   it is written.

3. **Stage and commit.** Unchanged.

4. **No remote.** The unpushed-work prompt gains a third state: *"Not on GitHub yet — this
   project only exists on this computer."*

5. **Connect.** Previews `git remote add origin <url>`, alongside a button that opens
   `github.com/new` and a box for the URL. Its "What could go wrong" names the trap directly:
   **create the repository empty — no README, no .gitignore** — or the first push is rejected.

6. **Send.** The existing `push` action, which already issues `--set-upstream` on first use.

## Explaining local versus remote

Through the existing content system, not a bespoke screen, so the explanation arrives at the
moment of decision rather than in a tutorial the user skips.

`remote` and `upstream` are already glossary terms. This adds:

- **`local repository`** — the `.git` folder on this machine; the thing `init` creates.
- **`origin`** — a nickname for a remote, not a magic word.
- **`GitHub`** — a company that hosts copies of repositories. Not git. The distinction most
  beginners never get told.

The `connect-remote` content carries the main explanation in its four headings.

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
`RepoState` full of nulls. It stays small: exactly two operations live there.

### Two kinds of operation

| Operation | Kind | Why |
|---|---|---|
| `init-repository` | Setup operation | No repository exists yet |
| `create-gitignore` | Setup operation | **Not a git command at all** — a file write |
| `connect-remote`, `disconnect-remote` | Ordinary `GitAction` | Repository exists; plain argv |

`push` is unchanged and already issues `--set-upstream` on first use.

`create-gitignore` is why `SetupPreview` carries **either** a `CommandLine` **or**
`FileContents`. The panel renders whichever is present: "The command" becomes "The file",
showing the commented template. No fake command is invented, and no heading is silently
dropped.

`disconnect-remote` exists so that `connect-remote`'s "How to undo this" is a real action
rather than a described one.

### Argument injection

The remote URL is pasted by the user and lands in argv. Argv arrays prevent *shell* injection
but not *argument* injection: a value beginning with `-` is read by git as a flag, and
`git remote add origin --upload-pack=…` is a real attack shape.

`RequiresValidRemoteUrl` therefore rejects anything that is not `https://…` or `git@…`, and
rejects leading dashes outright, before `BuildArgs` is reached.

### Components

**`GitHelper.Core`**

- `FolderState( Path, IsRepository, FileCount, HasGitignore, ProjectType )`, produced by
  `FolderInspector`.
- `ProjectType` — `DotNet | Node | Python | Java | Generic`.
- `SetupService` — `PreviewAsync` / `RunAsync`, mirroring `ActionService`'s shape so the panel
  can treat both alike.
- `SetupPreview` — the same four explanation blocks, plus `CommandLine` or `FileContents`.
- `connect-remote` and `disconnect-remote` action descriptors.
- `RequiresNoRemote`, `RequiresValidRemoteUrl`.
- `ActionRequest` gains `RemoteUrl`; `SlotBinder` gains a `remoteUrl` slot.

**`GitHelper.Content`**

- Five `.gitignore` templates as embedded resources, each short and commented.
- Content files for `init-repository`, `create-gitignore`, `connect-remote`,
  `disconnect-remote`.
- Glossary terms `local-repository`, `origin`, `github`.

**`GitHelper.App`**

- `StartupViewModel` — the dead-end error becomes a `FolderIsNotARepository` state carrying a
  `FolderState`.
- `ExplainPanelViewModel` gains `ShowSetupAsync`, so there remains exactly one panel.
- `ChangesViewModel` — a third push-prompt state, and the `.gitignore` offer banner.
- `IBrowserLauncher` — a testable seam for opening `github.com/new`, mirroring `IFolderPicker`.

### Data flow

```
pick folder → FindRepoRootAsync = null → FolderInspector
   → StartupState.FolderIsNotARepository
   → "Start tracking" → panel previews `git init -b main` → confirm → run
   → open normally, via the existing RepositoryOpenedAsync path

refresh → MainViewModel inspects the repo root → FolderState
        → ChangesViewModel.Update(state, folderState)
   → no .gitignore? → offer banner, template chosen by ProjectType
   → no remote?     → "Not on GitHub yet" → connect-remote → push -u
```

**Who runs the inspection after the repository exists.** The `.gitignore` banner needs
`HasGitignore` and `ProjectType`, which live on `FolderState`, not `RepoState` — but by then
the app is past the startup screen. `FolderInspector` reads any folder, including a repository
root, so `MainViewModel.RefreshAsync` performs the inspection alongside the state read and
passes both to `ChangesViewModel`. The viewmodel does no filesystem work itself, matching how
it already receives `RepoState` rather than reading it.

## Error handling

Most cases fall out of the existing translator. Three are new:

- **Never overwrite an existing `.gitignore`.** If one appears between the offer and the run,
  refuse and say so. Clobbering a user's file is not undoable by anything the app can show.
- **Non-fast-forward on first push** needs its own translator entry. This is the case where the
  user ticked "Add a README" while creating the repository. Raw git blames a "non-fast-forward",
  which a beginner cannot decode; the message must name the actual cause and the way out.
- **The credential prompt must be announced.** Git Credential Manager opens its own browser
  window on first push. Unwarned, that reads as the app asking for a GitHub password.

Existing behaviour covers the rest: `init` failing on permissions, a remote that already
exists, an unreachable network.

## Testing

- `FolderInspector` detection, table-driven across the five project types.
- Every `ProjectType` maps to a shipped template; every template is non-empty and commented.
- An existing `.gitignore` is never overwritten.
- URL validation: rejects a leading `-`, accepts `https://` and `git@`, rejects junk.
- `connect-remote` builds the expected argv.
- `init` against a temp directory produces a real repository.
- The push prompt's third state.
- One journey test: empty directory → init → gitignore → stage → commit → connect → assert
  `origin` is set. It stops short of a real network push.

## Out of scope

- Creating the GitHub repository itself, now or later, unless the no-credentials rule changes.
- Creating the folder as well as the repository ("New project"). One entry point covers both
  cases; a second can be added later if the single one proves confusing.
- SSH key setup, `.gitattributes`, and choosing a licence.
- Cloning an existing remote repository. That is a sibling feature, not part of this one, and
  deserves its own pass.
