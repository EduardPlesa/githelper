# GitHelper — Tags and Stash (v1.1)

**Date:** 2026-08-03
**Status:** Approved design, ready for implementation planning
**Roadmap:** [docs/roadmap.md](../../roadmap.md) — Bucket 1, the remaining v1.1 items now that
remote management has shipped.

## Purpose

Closes out v1.1: tags and stash, the two remaining Bucket 1 gaps. Both fit the existing
descriptor model exactly — one `GitAction` plus one content file per action, no new flow shape.
The roadmap's own caveat applies to both: "no new UI code" holds for the action, not for the
objects. `RepoState` has no notion of a tag or a stash today, so each needs new read state, a
parser, and somewhere in the UI to show up.

## Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Tag kind | Lightweight only, always targets `HEAD` | An annotated-vs-lightweight choice, or a commit picker, is a second concept and a new UI paradigm (picking an arbitrary commit from history) for a v1.1 item that is supposed to cost "one descriptor, one content file." Tagging the commit you are on is the case a beginner has. |
| Stash conflict handling | `stash-pop` / `stash-apply` require a clean working tree first | The whole app assumes an action is atomic (preview → run → narrate → done). A stash pop against a dirty tree can leave conflict markers — that is operation state, which roadmap Bucket 2 explicitly defers. Requiring `RequiresNoUncommittedChanges` first makes the pop/apply always a fast-forward-shaped, conflict-free operation, so v1.1 never has to touch operation state. |
| Stash scope | Tracked changes only (`git stash push`, no `-u`) | Matches git's own default. Untracked-file stashing is a separate, less-common case not worth a second precondition and a second explanation for v1.1. |
| UI placement | Embedded sections in existing tabs, not new tabs | Tags are refs, like branches — added to the Branches tab. Stash acts on uncommitted changes — added to the Changes tab. Avoids growing `MainTab` and keeps the nav surface stable; matches how remote management landed inside Changes/Branches rather than as its own tab. |
| Tag deletion danger | `Danger.Caution` | Unlike `branch -d`, `tag -d` has no git-side safety check — it always succeeds, even for a tag nowhere else. The app cannot rely on git's refusal here the way `delete-branch` does. |
| Stash drop danger | `Danger.Destructive` | Same shape as `discard-file`: once dropped, the stashed changes are gone with no undo. |

### Rejected alternatives

- **Annotated tags with a message field.** Doubles the tag concept for a beginner tool; deferred
  indefinitely, not just out of v1.1.
- **Tagging an arbitrary commit from History.** Needs a per-commit action affordance that does
  not exist anywhere in the app yet — that is new UI paradigm, which Bucket 1 is defined to avoid.
- **New `MainTab.Tags` / `MainTab.Stash` entries.** Considered and rejected in favor of embedding
  — see Decisions above.
- **Letting `stash-pop` run against a dirty tree and translating the conflict.** Pulls operation
  state into v1.1 by the back door. Declined the same way merge/rebase is deferred to v2.

## Architecture

Both features are ordinary `GitAction`s. No new operation kind, no change to the
preview/run/narrate flow.

### `GitHelper.Core`

**Model**

- `TagInfo(string Name, string Target)` — `Target` is the short hash the tag points at, for
  display only.
- `StashInfo(string Ref, string Message)` — `Ref` is git's own selector (`stash@{0}`), which is
  what every stash action passes back to git; it is never parsed or re-derived.
- `RepoState` gains `Tags` and `Stashes`, both `IReadOnlyList<...>`, following the same shape as
  `Branches`.

**Parsing**

- `TagParser`, mirroring `BranchParser` exactly: `for-each-ref --format=%(refname:short)%09%(objectname:short) refs/tags`, tab-split, skip blank lines.
- `StashParser`: `stash list --format=%gd%x09%s`, tab-split into `Ref` and `Message`. Stash
  entries are commits under the hood, so `--format` works the same way it does for `log`.

**`RepoStateReader`** gains two more calls alongside the existing status/log/branch/remote reads:
`for-each-ref ... refs/tags` and `stash list --format=...`. A repository with no tags or no
stashes returns empty output, not a failure — no special-casing needed (same as branches today).

**Actions** (`ActionCatalog`)

| Id | Danger | Argv | Preconditions | Undo |
|---|---|---|---|---|
| `create-tag` | Safe | `tag <name>` | `RequiresTagName`, `RequiresCommits`, `RequiresTagDoesNotExist` | `delete-tag` |
| `delete-tag` | Caution | `tag -d <name>` | `RequiresTagName` | — |
| `stash` | Safe | `stash push` or `stash push -m <message>` | `RequiresUncommittedChanges` | `stash-pop` |
| `stash-pop` | Caution | `stash pop <ref>` | `RequiresStashRef`, `RequiresNoUncommittedChanges` | — |
| `stash-apply` | Caution | `stash apply <ref>` | `RequiresStashRef`, `RequiresNoUncommittedChanges` | — |
| `stash-drop` | Destructive | `stash drop <ref>` | `RequiresStashRef` | — |

`stash`'s Safe rating (rather than Caution) mirrors `stage-all`/`unstage-all`: reversible via its
own undo action, nothing is lost. `stash-pop`/`stash-apply` are Caution despite being
conflict-free by precondition, because they change the working tree in a way the user did not
just type — same tier as `switch-branch`.

**New preconditions** (`Preconditions.cs`)

- `RequiresTagName` — mirrors `RequiresBranchName`.
- `RequiresTagDoesNotExist` — mirrors `RequiresBranchDoesNotExist`, checked against `state.Tags`.
- `RequiresUncommittedChanges` — the inverse of the existing `RequiresNoUncommittedChanges`;
  checks `state.HasUncommittedChanges`. Fails with a message pointing out there is nothing to set
  aside.
- `RequiresStashRef` — checks `request.StashRef` is present, mirrors `RequiresPath`.
- `RequiresNoUncommittedChanges` already exists (used today by `switch-branch`) and is reused
  as-is for `stash-pop`/`stash-apply`.

**`ActionRequest`** gains two fields: `TagName` and `StashRef`. The existing `Message` field is
reused for the optional stash message on `stash` — no new field needed, since a commit message
and a stash message occupy the same "short text describing this" slot and are never both in play
on the same request.

### `GitHelper.Content`

- Action content files: `create-tag.md`, `delete-tag.md`, `stash.md`, `stash-pop.md`,
  `stash-apply.md`, `stash-drop.md` — each with `what` / `risks` / `undo`, following the existing
  four-heading convention.
- Glossary terms: `tag` and `stash`, referenced as `[[tag]]` / `[[stash]]` from the six action
  files above and cross-linked from each other the way `branch` and `unmerged-branch` are today.

### `GitHelper.App`

**Branches tab** (`BranchesViewModel`, `BranchesView`)

- New `ObservableCollection<TagRowViewModel> Tags`, populated in `Update(RepoState)` the same way
  `Branches` is.
- `TagRowViewModel(TagInfo, Func<string,string,Task> invokeAction)` — mirrors
  `BranchRowViewModel`: exposes `Name`, `TargetLabel`, and a `DeleteCommand` wired to
  `delete-tag`. No `CanDelete` gating needed — unlike a branch, there is no "current tag" a user
  could be on.
- A `NewTagName` text box and `CreateTagCommand`, wired the same way
  `NewBranchName`/`CreateBranchCommand` are today. Clears on the same "did it observably appear"
  check `OnActionCompleted` already does for branch names.

**Changes tab** (`ChangesViewModel`, `ChangesView`)

- New `ObservableCollection<StashRowViewModel> Stashes`, populated in `Update`.
- `StashRowViewModel(StashInfo, Func<string,string,Task> invokeAction)` — exposes `Message`,
  `RefLabel`, and `PopCommand` / `ApplyCommand` / `DropCommand`, each wired to the matching action
  id with the row's `Ref`.
- A `StashMessage` text box (optional — empty is a valid `stash` call) and `StashCommand`.
- `StashCommand`'s enabled state follows `state.HasUncommittedChanges`, the same way other
  change-dependent controls on this tab already gate on repository state.

### Data flow

```
refresh → RepoStateReader.ReadAsync
   → + for-each-ref refs/tags   → TagParser   → RepoState.Tags
   → + stash list --format=...  → StashParser → RepoState.Stashes
   → BranchesViewModel.Update   → Tags section
   → ChangesViewModel.Update    → Stash section
```

No change to `MainViewModel.RefreshAsync`'s shape — it already calls `Update` on every tab
viewmodel after each read; `Branches.Update` and `Changes.Update` just have more to do with the
same `RepoState`.

## Error handling

Both features stay inside cases git already refuses cleanly, so no new `ErrorTranslator` entries
are anticipated:

- `create-tag` against an existing name is caught by `RequiresTagDoesNotExist` before git ever
  runs.
- `stash pop`/`apply` are only ever invoked against a clean tree (precondition-enforced), so the
  one git error that matters here — applying onto conflicting local changes — cannot occur through
  this UI.
- `stash drop`/`pop` against a stash that has meanwhile been dropped by something outside the app
  (e.g. the CLI, in another window) is the one real failure mode. It surfaces as git's own "No
  stash entries found" — this needs one new translator entry: told plainly, with a suggestion to
  refresh, rather than shown raw.

## Testing

- `TagParser` / `StashParser`: empty output, one entry, multiple entries, matching the existing
  `BranchParserTests` shape.
- `ActionCatalogTests`: argv for all six new actions, including the message/no-message branch of
  `stash`.
- `PreconditionTests`: `RequiresTagDoesNotExist` blocks a duplicate name; `RequiresUncommittedChanges`
  blocks stashing a clean tree; `RequiresStashRef` blocks pop/apply/drop with nothing selected;
  `RequiresNoUncommittedChanges` blocks pop/apply against a dirty tree.
- `ContentIntegrityTests` (existing, generic): confirms all six new content files exist, and both
  new glossary terms resolve — no new test needed, just new fixtures for it to walk.
- `BranchesViewModelTests` / `ChangesViewModelTests`: a create-tag / delete-tag round trip and a
  stash / pop round trip against a real temp repository, mirroring the existing branch and
  push-prompt journey tests.

## Out of scope

- Annotated tags, and tagging any commit other than `HEAD`.
- Pushing or fetching tags (`git push --tags`) — tags stay local-only for v1.1, same boundary
  remote management drew before this spec existed.
- Stashing untracked files (`stash -u`) or specific paths (`stash push -- <path>`).
- Resolving a conflicting stash pop — precondition-blocked instead; real support arrives with
  Bucket 2/4 (operation state, guided conflict resolution).
- Renaming a tag. Delete and recreate covers it, with fewer concepts.
