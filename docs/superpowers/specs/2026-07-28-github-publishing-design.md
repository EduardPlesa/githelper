# GitHelper — Publishing a repository to GitHub

**Date:** 2026-07-28
**Status:** Approved design, ready for implementation planning
**Sibling:** [Creating a repository locally](2026-07-28-local-repository-setup-design.md) — the
first half. This spec assumes a repository already exists, however it was created.

## Purpose

A local repository is invisible to everyone else and survives nothing worse than a lost laptop.
This adds the step that puts it on GitHub — and, more importantly for a beginner, explains what
that step actually means.

The teaching goal matters as much as the feature: most beginners never get told that git and
GitHub are different things.

## Decisions

| Decision | Choice | Rationale |
|---|---|---|
| How far into GitHub | App wires up; the user creates the empty repository on github.com | Creating it for them needs a personal access token. The app's standing rule is that it never collects, stores, or transmits credentials, and no view may contain a token field. That rule is worth more than the convenience. |
| Flow shape | Ordinary actions, surfaced by repository state | Consistent with the rest of the app, and with the sibling spec. |
| First-push credentials | Git's own credential helper, warned about in advance | Git Credential Manager opens its own browser sign-in. The app must say this is coming, or it looks like the credential prompt it promised never to build. |
| Undo | A real `disconnect-remote` action | So `connect-remote`'s "How to undo this" is something the user can click, not merely a sentence. |

### Rejected alternatives

- **Creating the GitHub repository via the API.** Requires a token field the specs forbid.
- **A dedicated publish wizard.** A second UI paradigm; the four-heading teaching structure does
  not fit a wizard step.
- **Storing the remote URL in app settings.** Git already stores it. A second copy can disagree
  with the first.

## The journey

Continues from the sibling spec. Each step is a state-driven offer; nothing chains
automatically.

1. **No remote.** The unpushed-work prompt on the Changes tab gains a third state:
   *"Not on GitHub yet — this project only exists on this computer."*

2. **Connect.** Previews `git remote add origin <url>`, alongside a button that opens
   `github.com/new` and a box for the URL. Its "What could go wrong" names the trap directly:
   **create the repository empty — no README, no .gitignore** — or the first push is rejected.

3. **Send.** The existing `push` action, which already issues `--set-upstream` on first use.

## Teaching local versus remote

Through the existing content system, not a bespoke screen, so the explanation arrives at the
moment of decision rather than in a tutorial the user skips.

`remote` and `upstream` are already glossary terms, and `local repository` comes from the
sibling spec. This adds:

- **`origin`** — a nickname for a remote, not a magic word. Beginners assume it means something
  official.
- **`GitHub`** — a company that hosts copies of repositories. Not git, and not required to use
  git. This is the distinction the whole feature exists to teach.

The `connect-remote` content carries the main explanation across its four headings.

## Architecture

Everything here is an ordinary `GitAction`. This spec introduces no new operation kind — the
`SetupService` machinery from the sibling spec is not needed and not used.

### Components

**`GitHelper.Core`**

- `connect-remote` — `git remote add origin <url>`, `Danger.Caution`, undo `disconnect-remote`.
- `disconnect-remote` — `git remote remove origin`, `Danger.Caution`.
- `RequiresNoRemote` — connecting twice is a confusing failure otherwise.
- `RequiresValidRemoteUrl` — see below.
- `ActionRequest` gains `RemoteUrl`; `SlotBinder` gains a `remoteUrl` slot.
- A new error-translator entry for non-fast-forward on first push.

**`GitHelper.Content`**

- Content files for `connect-remote` and `disconnect-remote`.
- Glossary terms `origin` and `github`.

**`GitHelper.App`**

- `ChangesViewModel` — third push-prompt state, plus the URL box and the "open GitHub" button.
- `IBrowserLauncher` — a testable seam for opening `github.com/new`, mirroring `IFolderPicker`.
  The composition root supplies the real one; tests supply a stub and assert the URL.

### Argument injection

The remote URL is pasted by the user and lands in argv. Argv arrays prevent *shell* injection
but not *argument* injection: a value beginning with `-` is read by git as a flag, and
`git remote add origin --upload-pack=…` is a real attack shape.

`RequiresValidRemoteUrl` therefore rejects anything that is not `https://…` or `git@…`, and
rejects leading dashes outright, before `BuildArgs` is reached. The rejection message is
written for a beginner who has pasted the wrong thing — the GitHub web page URL rather than the
clone URL is the common mistake — not for someone attempting an attack.

### Data flow

```
refresh → ChangesViewModel.Update(state, …)
   → HasRemote false → "Not on GitHub yet"
        → [Open github.com/new] → IBrowserLauncher
        → paste URL → connect-remote → preview → confirm → run
   → remote now set, no upstream → "This branch is not on the server yet"
        → push → --set-upstream origin <branch>
```

The second state already exists and needs no change; it begins working the moment a remote is
present.

## Error handling

- **Non-fast-forward on first push** needs its own translator entry. This is the case where the
  user ticked "Add a README" while creating the repository. Raw git blames a "non-fast-forward",
  which a beginner cannot decode; the message must name the actual cause and the way out.
- **The credential prompt must be announced** in the `push` content before the first push, so
  Git Credential Manager's browser window is expected rather than alarming.
- **Authentication refused** is already translated, and already states that the app never
  handles the password. That wording stays exactly as it is.
- **A URL that is valid but wrong** — a typo'd repository name — fails at push, not at connect.
  The translator entry for a missing repository should say that the remote can be changed, and
  point at `disconnect-remote`.

## Testing

- URL validation: rejects a leading `-`, rejects a GitHub web page URL with a readable
  explanation, accepts `https://…` and `git@…`.
- `connect-remote` and `disconnect-remote` build the expected argv.
- `RequiresNoRemote` blocks a second connect.
- The push prompt's third state appears only with no remote at all.
- `IBrowserLauncher` receives `https://github.com/new`.
- One journey test: repository with a commit → connect → assert `origin` is set and the branch
  is unchanged. It stops short of a real network push.

## Out of scope

- Creating the GitHub repository itself, now or later, unless the no-credentials rule changes.
- More than one remote. `origin` is the only remote this app manages.
- SSH key generation or setup.
- Cloning an existing remote repository — a sibling feature deserving its own pass.
- Renaming or re-pointing an existing remote. Disconnect and reconnect covers it, with fewer
  concepts.
