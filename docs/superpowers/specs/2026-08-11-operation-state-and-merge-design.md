# Operation State and Merge (v2) — Design

**Status:** approved, in implementation
**Date:** 2026-08-11
**Order:** built **before** v1.1 (tags and stash), against `RepoState` as it stands on
`main`. Both change `RepoState`, which is a positional record, so both touch every
construction site in the solution — whichever lands second absorbs that cost. The tags and
stash plan will need rewriting against the shape this leaves behind.

---

## What this is

The roadmap calls operation state the one real architectural gap: every action today is
atomic — preview, run, narrate, done — and `git merge` breaks that by stopping mid-way and
leaving the repository in a state the user must drive to completion or abandon.

This design adds operation state to the model, and adds merge as the operation that proves
it works.

## What this is not

**Rebase is out of scope.** It rides on the same foundation, adds a sequencer that stops
repeatedly, and introduces `--skip` and step-of-total progress. It gets its own spec once
this one has shipped and the foundation has been used in anger.

**The `Danger` enum is not touched.** It stays three-valued, and the destructive modal keeps
its discard-file-shaped consequence sentence. The roadmap flags both as strain points that
v2 would expose; on inspection only rebase and force-push actually force the change, and
neither is here. Generalising the modal now would be building against a shape we cannot yet
see.

**No diff viewer.** That is v2.5. Conflicts are fixed in the user's own editor, and this
design is explicit with the user about that rather than pretending otherwise.

---

## Operation state

```csharp
public enum OperationKind { Merge }          // grows: Rebase, CherryPick, Revert

/// <summary>
/// An operation git has started and not finished. Null on RepoState means nothing is in
/// flight. IncomingLabel is for copy only and may be null when the merged ref has no name.
/// </summary>
public sealed record OperationState(OperationKind Kind, string? IncomingLabel);
```

`RepoState` gains one field:

```csharp
OperationState? Operation
```

No step or total fields. Those are rebase's problem, and adding them now would mean two
dead fields that every merge sets to zero.

### Detection

Read through git, not by poking at `.git`:

```
git rev-parse -q --verify MERGE_HEAD
```

Exits 0 exactly while a merge is in progress, and prints the incoming commit hash. It exits
non-zero otherwise, so no output parsing is needed to answer the yes/no question.

Only when that succeeds, a second call turns the hash into a name for the copy:

```
git name-rev --name-only <hash>
```

This costs one extra subprocess per refresh in the common case. It is preferred over a
`File.Exists` check on `.git/MERGE_HEAD` because it is correct in linked worktrees, where
`.git` is a file rather than a directory, and because it keeps the rule that git is the
source of truth about the repository.

`name-rev` returns the literal string `undefined` when the merged commit has no reachable
name. `IncomingLabel` is null in that case and the copy falls back to "another branch".

Because the state is read from the repository on every refresh and never cached, it
survives closing and reopening the app for free — which it must, since the repository
state does.

### A latent bug this surfaces

`StatusParser` sets **both** `IndexChange` and `WorkTreeChange` to `ChangeKind.Unmerged`
for a conflicted file. `FileChange.IsStaged` is therefore true for conflicted files today,
which means:

- conflicted files render in the Staged section of the Changes tab, and
- `HasStagedChanges` is true mid-merge, so Commit lights up when it must not.

`FileChange` gains `IsUnmerged`, and its two existing predicates exclude that case rather
than a caller having to remember to:

```csharp
public bool IsUnmerged => IndexChange == ChangeKind.Unmerged;

public bool IsStaged => IndexChange is not (ChangeKind.None or ChangeKind.Unmerged);

public bool HasUnstagedChanges =>
    WorkTreeChange is not (ChangeKind.None or ChangeKind.Untracked or ChangeKind.Unmerged);
```

`RepoState` gains `Unmerged` alongside `Staged` / `Unstaged` / `Untracked`, and
`HasStagedChanges` follows `IsStaged` as it already does.

`HasUncommittedChanges` must stay **true** during a merge — there genuinely are
uncommitted changes — so it becomes `Changes.Any(c => c.IsStaged || c.HasUnstagedChanges ||
c.IsUnmerged)`. This matters because `RequiresNoUncommittedChanges` guards actions that
must not run mid-merge.

---

## Actions

All four are ordinary `GitAction` descriptors plus content files. The roadmap's worry that
resume actions "do not fit the current action shape" turns out to be about where the button
lives, not about the descriptor: nothing in `GitAction`, `ActionService`, `ActionRequest`,
or the explain panel changes shape to accommodate them.

| id | argv | danger | preconditions |
|---|---|---|---|
| `merge` | `merge --no-edit <branch>` | Caution | `RequiresBranchName`, `RequiresNotCurrentBranch`, `RequiresNoUncommittedChanges`, `RequiresNoOperationInProgress` |
| `mark-resolved` | `add <path>` | Safe | `RequiresPath`, `RequiresMergeInProgress` |
| `merge-continue` | `merge --continue` | Caution | `RequiresMergeInProgress`, `RequiresNoUnmergedFiles` |
| `merge-abort` | `merge --abort` | Caution | `RequiresMergeInProgress` |

`--no-edit` is passed explicitly rather than relying on git's tty detection.

`mark-resolved` shares argv with `stage-file` and is still a separate id, because the
content id equals the action id and the explanation is a different sentence: this one is
about telling git you have removed the conflict markers, not about choosing what goes into
the next commit.

**`merge-abort` is `Caution`, not `Destructive`.** Aborting after hand-resolving several
files does throw that work away unrecoverably, which argues for the modal. It stays
`Caution` because the modal's consequence sentence is hardcoded for `discard-file`, and
generalising it is the strain point this version is deliberately not paying for. The `risks`
content says plainly that fixes already made are lost.

Three new preconditions: `RequiresMergeInProgress`, `RequiresNoOperationInProgress`,
`RequiresNoUnmergedFiles`.

### One existing action must be guarded

`git commit` run while `MERGE_HEAD` exists **finalises the merge**. The Changes tab's
Commit button would therefore quietly complete a merge the user is part-way through, with a
message they wrote for something else and an explanation panel describing an ordinary
commit. `commit` gains `RequiresNoOperationInProgress`, so finishing a merge only ever
happens through **Finish the merge**, which says what it is doing.

`switch-branch`, `pull`, and `create-branch` need no new guard: git refuses them itself
mid-merge, and the existing error translator handles the refusal. Only `commit` is
dangerous specifically because it *succeeds*.

`merge` has no `UndoActionId`. Undoing a completed merge means rewriting history, which is a
v2.1 concern; the `undo` content explains what to do instead.

### One change to `GitRunner`

Add to the environment, beside the existing `GIT_TERMINAL_PROMPT=0`:

```csharp
psi.Environment["GIT_EDITOR"] = "true";
```

Same justification as the existing line: git must never block waiting on something the user
cannot see. Without it, `merge --continue` can hang on a configured editor. Setting it in
the runner rather than as `-c core.editor=true` in `BuildArgs` keeps the command log
teachable — it shows `git merge --continue`, which is the command the user should learn.

---

## A stopped merge is not a failure

This is the sharp edge of the whole design.

`git merge` exits **non-zero** when it stops on conflicts. Today that routes straight into
`ExplainPanelState.Error` with a translated error, which is a lie: git did exactly what it
was asked to do, and the user has work to do rather than a problem to fix.

`ActionOutcome` gains one computed member:

```csharp
/// <summary>
/// True when the command left an operation in flight that was not in flight before.
/// Derived from observed state rather than from the exit code, because a merge that stops
/// on conflicts exits non-zero and is not a failure.
/// </summary>
public bool Paused => Before.Operation is null && After.Operation is not null;
```

`Success` keeps meaning what it means today — the exit code. The explain panel checks
`Paused` before `Error`, so a stopped merge shows narration and the band rather than a red
box.

**Known limitation, stated rather than solved:** a merge started outside the app, or one
already paused when the repository is opened, produces no `Paused` outcome, because nothing
ran. The band still appears — it renders from `RepoState.Operation`, not from an outcome.
`Paused` only ever describes what a run just did.

## Narration

`Narrator` gains `DescribeOperation(before, after)`, staying inside the existing discipline
of describing the observed difference rather than the intended action:

| transition | sentence |
|---|---|
| none → merge | "The merge stopped. N file(s) have changes git could not combine on its own." |
| merge → none, commits added | "The merge is finished." (the commit sentence already covers the rest) |
| merge → none, no commits added | "The merge was abandoned. Your files are back as they were." |

A fast-forward merge produces no operation state at all and no stop — the new commits show
up in `RecentCommits` and the existing commit and sync sentences already describe it
correctly.

---

## User interface

### The band

New `OperationBannerViewModel`, owned by `MainViewModel` and updated from the same
`RefreshAsync` that already republishes state to every tab. Rendered in `MainWindow`
between the header and the tab `ContentControl`, so it is visible on Changes, History, and
Branches alike — a paused merge is a property of the repository, not of one tab.

- Headline: "You are part-way through merging `feature` into `main`."
- Detail, while files conflict: "3 files have changes git could not combine. Open each one,
  fix the marked sections, then mark it fixed below."
- Detail, once none remain: "All conflicts fixed. Finish the merge to save it as a commit."
- **Finish the merge** — `merge-continue`, disabled while unmerged files remain.
- **Abandon the merge** — `merge-abort`, always available.

Both buttons route through `Explain.ShowAndRunIfUngatedAsync`, so explain → confirm →
narrate is the existing pipeline with no new branches in it. The band is presentation only
and owns no git logic.

No tab is disabled while an operation is paused. History is read-only and harmless, and
hiding it would be paternalistic; actions git would refuse are already blocked by their own
preconditions, which is where that rule belongs.

### Changes tab

Unmerged files get their own section above Staged, now that they are no longer counted as
staged. Each row replaces Stage / Unstage / Discard with a single **I fixed this one**
button. `FileChangeRowViewModel` gains `IsUnmerged` and `MarkResolvedCommand`; its existing
`DescribeKind` already renders `Unmerged` as "conflicted".

### Branches tab

Each non-current row gains **Bring into `main`**, invoking `merge` with that branch's name.
Disabled on the current branch, for the same reason Switch and Delete already are. The
existing `Func<string, string, Task> invokeAction` delegate already carries a branch name,
so no new plumbing is needed.

---

## Content

Four action files, each with `what` / `risks` / `undo`, picked up by the existing wildcard
in `GitHelper.Content.csproj` and enforced by `ContentIntegrityTests`:

- `src/GitHelper.Content/actions/merge.md`
- `src/GitHelper.Content/actions/mark-resolved.md`
- `src/GitHelper.Content/actions/merge-continue.md`
- `src/GitHelper.Content/actions/merge-abort.md`

Two new glossary terms: `merge` and `conflict`. The existing `fast-forward` and
`unmerged-branch` terms are referenced, not rewritten.

One new slot, `mergingFrom`, bound from `state.Operation?.IncomingLabel`. It is derived
from state rather than from the request, which is why `ActionRequest` needs no new fields:
`merge` uses the existing `branchName`, and `merge-continue` / `merge-abort` take no
arguments at all.

---

## Testing

- **`RepoStateReaderTests`** — against a real repository: no operation on a clean repo, a
  merge in flight after a conflicting merge, none again after abort, and `IncomingLabel`
  reading back as the branch name.
- **`StatusParserTests` / `RepoStateTests`** — a conflicted file is `Unmerged` and is
  excluded from `Staged`, so `HasStagedChanges` is false mid-merge.
- **`NarratorTests`** — the three operation sentences.
- **`PreconditionTests`** — the three new preconditions, pass and fail.
- **`ActionCatalogTests`** — argv shape for each of the four, plus a full round trip against
  a real repository: create a conflict, merge stops, mark each file resolved, continue,
  merge commit exists.
- **`ActionServiceTests`** — a conflicting merge yields `Paused: true`, `Success: false`,
  `Error: null`; and `commit` is blocked while a merge is in flight.
- **App tests** — band hidden with no operation; shown with Finish disabled while files
  conflict; Finish enabled once none remain.

The one genuinely new fixture is a conflict builder in `TestRepo`: two branches editing the
same line of the same file.

---

## What this unlocks

Rebase (v2.1) reuses `OperationState`, the band, the `Paused` routing, and the whole
resolve loop, and adds only `OperationKind.Rebase`, step-of-total progress, and `--skip`.
Guided conflict resolution (v3) sits on this plus the v2.5 diff viewer, replacing "fix it
in your editor" with an in-app surface — without changing any of the state machine
underneath it.
