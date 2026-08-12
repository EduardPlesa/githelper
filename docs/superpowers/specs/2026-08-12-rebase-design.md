# Rebase (v2.1) — Design

**Status:** approved, not yet planned
**Date:** 2026-08-12
**Builds on:** v2 (operation state and merge). Everything here reuses `RepoState.Operation`, the
shell band, `ActionOutcome.Paused`, and the resume-actions-are-ordinary-descriptors finding.

---

## What this is

One journey: *"my branch is behind, bring it up to date."* `git rebase <base>` on the current
branch, plus continue, skip and abort.

## What this is not

- **No interactive rebase.** Reordering, squashing and rewording are expert affordances.
- **No arbitrary `--onto`.** One base branch, picked from the branch list.
- **No force-push**, and therefore **no rebasing a branch that is already on a server** (below).
- **No `Danger` enum change.** The roadmap predicted rebase would force it. It does not — see
  "The consequence sentence moves to content".

### Why rebase at all, given merge shipped

Worth recording, because the roadmap declined hunk staging on exactly this reasoning and the
same argument nearly lands here. Rebase rewrites history and is harder to explain than hunk
staging; merge already unsticks a diverged branch, so rebase is not needed to rescue anyone.

It earns its place only in the narrow form above: a branch that has never left this computer,
brought up to date with its base, producing a straight history rather than a merge commit the
user did not ask for. That is the same instinct behind `pull --ff-only`. Anything wider is an
expert affordance and stays out.

---

## Refusing the trap

**New precondition: `RequiresNoUpstream`.** Rebase is refused once the branch exists on a
server.

Rewriting commits that have been pushed makes the next push fail. This app has no force-push
and deliberately will not grow one by accident, so the user would be stranded — and stranded
with a *misleading* message: the existing `non-fast-forward` rule says "The server has work you
do not have yet" and sends them to pull, which merges the old commits back and makes it worse.

Refusing up front, with copy that explains why, is the honest version.

**This excludes the PR-branch workflow** — push a feature branch, then update it from main —
which is the most common real use of rebase. That is a genuine limitation, accepted knowingly.
It is the correct behaviour until force-push is designed on purpose rather than smuggled in as
rebase's dependency.

---

## Operation state grows a rebase-shaped field

```csharp
public enum OperationKind { Merge, Rebase }

public sealed record OperationState(
    OperationKind Kind,
    string? IncomingLabel,
    RebaseProgress? Rebase = null);

/// <summary>
/// Where a paused rebase has got to. Null for a merge, which has no steps — the same reason
/// v2 declined to add step/total up front rather than carry meaningless zeros.
/// </summary>
public sealed record RebaseProgress(int Step, int Total, string? StoppedAtSubject);
```

### Detection

Every fact below was verified against a real paused rebase before being written down.

| Need | Source |
|---|---|
| Is a rebase in flight | `git rev-parse --git-path rebase-merge`, resolved against the repo root, then directory exists |
| Older backend | same, for `rebase-apply` |
| Progress | `msgnum` / `end` in that directory (`next` / `last` for the apply backend) |
| Branch being rebased | `head-name` |
| Base it is going onto | `onto` (a SHA) → the existing `name-rev` helper for a friendly name |
| Commit it stopped on | `git log -1 --format=%s REBASE_HEAD` |

**`MERGE_HEAD` is not set during a rebase conflict** — confirmed, not assumed — so merge and
rebase detection cannot be confused. Rebase is still checked first, so that if a future git ever
sets both, the more specific answer wins.

**This reads git's own files, which is new.** The v2 objection to probing `.git/MERGE_HEAD` was
that a hardcoded path is wrong in a linked worktree; asking git where the file is removes
exactly that objection, and nothing else exposes rebase progress — porcelain status carries none
and the long format is human text.

`msgnum` and `end` are still not documented API. If either is missing or unparseable, the rebase
is reported **without** progress rather than with a guess: `RebaseProgress` is null, and the band
drops the counter. Detection never depends on them.

---

## Actions

| id | argv | danger | preconditions |
|---|---|---|---|
| `rebase` | `rebase <base>` | Caution | `RequiresBranchName`, `RequiresNotCurrentBranch`, `RequiresCommits`, `RequiresNoUncommittedChanges`, `RequiresNoOperationInProgress`, `RequiresNoUpstream` |
| `rebase-continue` | `rebase --continue` | Caution | `RequiresRebaseInProgress`, `RequiresNoUnmergedFiles` |
| `rebase-skip` | `rebase --skip` | **Destructive** | `RequiresRebaseInProgress` |
| `rebase-abort` | `rebase --abort` | Caution | `RequiresRebaseInProgress` |

Three new preconditions: `RequiresRebaseInProgress`, `RequiresNoUpstream`, and
`RequiresOperationInProgress` (below). `RequiresMergeInProgress` stays exactly as it is —
`merge-continue` and `merge-abort` must still refuse during a *rebase*, so it must not be
broadened.

`GIT_EDITOR=true` is already set on every invocation, so `rebase --continue` cannot hang waiting
for a commit-message editor — the same reason it was added for `merge --continue`.

### One existing action must change

**`mark-resolved` carries `RequiresMergeInProgress`.** During a rebase conflict that refuses,
leaving the user holding conflicted files with no way to mark them fixed and no way to continue.

It swaps to a new operation-agnostic `RequiresOperationInProgress` — satisfied by any
`Operation`, of any kind. `RequiresMergeInProgress` is not changed in place, because
`merge-continue` and `merge-abort` genuinely do need the merge-specific version: offering
"Finish the merge" during a rebase would be wrong.

`commit` already carries `RequiresNoOperationInProgress` and covers rebase unchanged.

### `rebase-skip` is the first commit-destroying action

`discard-file` and `stash-drop` destroy uncommitted work. `rebase-skip` drops a commit the user
wrote and described. It is recoverable through the reflog, but not through this app, so the copy
must not imply otherwise.

---

## The consequence sentence moves to content

`ExplainPanelViewModel.BuildConsequence` is a switch on action id holding user-facing copy. It
has two cases; `rebase-skip` would make three, and nothing stops a fourth Destructive action
silently inheriting `discard-file`'s sentence through the default arm.

- `ExplanationDocument` gains `Consequence`, parsed from a `## consequence` section.
- `ContentIntegrityTests` requires every `Danger.Destructive` action to have a non-empty one.
- `BuildConsequence` is deleted. The panel renders the block through `SlotResolver`, like
  `what` / `risks` / `undo`, so the sentence can name the real file, stash or commit.
- `discard-file.md` and `stash-drop.md` take their sentences down from the viewmodel.

**The `Danger` enum stays three-valued.** The roadmap expected rebase to force it. It does not:
three levels still describe the *gate* correctly — no confirmation, inline confirm, modal — and
what actually needed generalising was hardcoded copy in a viewmodel. Adding enum levels would
encode a copy difference as a danger difference.

---

## The band becomes kind-aware

`OperationBannerViewModel` hardcodes `merge-continue` / `merge-abort` and merge wording. It now
selects on `Operation.Kind`:

| | Merge | Rebase |
|---|---|---|
| Headline | "You are part-way through merging `feature` into `main`." | "You are part-way through updating `feature` onto `main` — commit 3 of 7." |
| Extra line | — | "Stopped on your commit 'fix login bug'." |
| Finish | Finish the merge | Continue |
| Abandon | Abandon the merge | Abandon the update |
| Skip | hidden | **Skip this commit** |

Button labels and visibility become viewmodel properties, so `MainWindow.axaml` stops hardcoding
the strings. The third button binds `IsVisible` to `CanSkip`, false for merge.

With no `RebaseProgress`, the headline drops the counter and the stuck-commit line and reads
"You are part-way through updating `feature` onto `main`."

Skip is Destructive, so it goes through the modal like any other — the band is not a shortcut
past the gate, which the merge band already established for abort.

---

## Narration

`Narrator` says nothing today when the operation is unchanged. Rebase breaks that: continuing
past one stop *is* the observed change.

| transition | sentence |
|---|---|
| none → rebase | "The update stopped on your commit 'X'. N file(s) have changes git could not combine." |
| rebase → rebase, step increased | "Moved on to commit 4 of 7." |
| rebase → none, commits added | "Your branch is now up to date with `main`." |
| rebase → none, no commits added | "The update was abandoned. Your branch is back as it was." |

Which sentence applies is decided by what changed between snapshots, never by which action ran —
the existing discipline, extended rather than bent.

---

## Content

New action files: `rebase.md`, `rebase-continue.md`, `rebase-skip.md` (with `## consequence`),
`rebase-abort.md`. New term: `rebase`. Reuses `conflict`, `commit`, `branch`, `upstream`.

One new slot, `rebasingOnto`, bound from `state.Operation` rather than the request — the same
reason `mergingFrom` is, since continue, skip and abort take no arguments.

`rebase.md` carries the hardest idea in the app and has to earn it: your commits are not moved,
they are **replayed as new commits** with the same changes and different identities. That is
also the reason the app refuses once a branch is on a server, so the `what` and the refusal
explain each other rather than being two rules to memorise.

---

## Testing

- **`TestRepo.StartConflictingRebaseAsync`** — mirrors the merge fixture: two branches editing
  the same line, then `git rebase` left stopped.
- **Reader** — no operation on a clean repo; rebase detected; step and total read; stuck commit
  named; base named; gone after abort; and progress reported as null when `msgnum` is absent.
- **Preconditions** — `RequiresRebaseInProgress` and `RequiresNoUpstream`, passing and failing.
- **Catalog** — argv per action; the count moves 25 → 29; the Destructive set becomes
  `discard-file`, `stash-drop`, `rebase-skip`.
- **Real-repository round trips** — conflict → mark resolved → continue → branch updated and the
  base's commit present; and skip → that commit absent from the branch.
- **Content integrity** — the destructive-needs-consequence rule, proven by watching it fail
  with the section removed before it is written.
- **Band** — rebase shows Skip, merge hides it; counter absent when progress is null.
- **Narrator** — the four sentences.
- **`mark-resolved`** — works during a rebase conflict, which is the regression this would
  otherwise ship.

---

## Known limitations, stated rather than discovered

1. **A branch already on a server cannot be rebased.** The most common real use of rebase.
   Waiting on force-push being designed deliberately.
2. **Progress depends on undocumented files.** Degrades to no counter, never to a wrong one.
3. **A skipped commit is gone as far as this app is concerned.** The reflog still has it; the
   app offers no route back, and the copy says so instead of implying a recovery it will not
   provide.
