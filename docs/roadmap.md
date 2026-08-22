# GitHelper — Roadmap

**Status:** living document
**Last updated:** 2026-08-21

This records what GitHelper does *not* do yet, why, and in what order those gaps should close.
It exists so the absences read as decisions rather than oversights — and so that a decision made
once is not re-litigated every time someone notices a missing feature.

Two of the items below are **declined**, not deferred. Those are the most important entries here.

---

## The question this document answers

Should the deferred features become a second application, or later versions of this one?

**Later versions of this one.** Three reasons, in order of weight:

1. **The architecture already anticipates it.** `GitAction` is a data descriptor — id, title,
   danger, argv builder, preconditions, undo hint — and `GitHelper.Core` has no Avalonia
   reference. A second application would have to fork the git runner, the state reader, the error
   translator, the content library, and the explain panel in order to add features the existing
   extension point was built for.

2. **A second app contradicts the product thesis.** GitHelper is designed to make itself
   obsolete: the real command is always visible, and the command log teaches the CLI by exposure.
   Splitting into a beginner app and an advanced app forces a tool switch at precisely the moment
   the user is least confident. The ramp has to be continuous.

3. **The audience is the same person later in time**, not a different person. That is a version
   axis, not a product axis.

### Where a second *thing* does make sense

Not a second git application — a second **frontend**. `Core` has no UI dependency specifically so
that a CLI, a VS Code extension, or a different desktop shell could drive the same engine,
content, and safety rules. That is the split with real value, and the architecture has already
paid for it.

---

## The gaps, by architectural cost

The deferred list is not one decision. It is four, and they cost wildly different amounts.

### Bucket 1 — More of the same

**~~Tags, stash~~ (shipped), cherry-pick (clean), ~~remote management~~ (shipped).**

These fit the existing model exactly: one descriptor plus one content file, no new flow.

**The caveat worth naming:** "no new UI code" holds for the *action* but not for the *objects*.
`RepoState` models branches, commits, and file changes — it has no notion of stashes or tags. So
listing them means new read state, a new parser, and somewhere to put them. Budget a small tab,
not zero.

**Remote management** additionally brushes against authentication. The app has a standing rule
that it never handles credentials, and relies on git's own credential helper. Adding a remote is
fine; anything that would prompt for a password must remain git's job, not the app's.

**Remote management shipped first**, as `connect-remote` and `disconnect-remote`, because it
was the half of repository setup the sibling spec left open. It confirmed the estimate above:
two descriptors, two content files, two glossary terms, and no new UI paradigm — but it also
needed a precondition to validate a pasted URL, which is the first time argv has carried a
value straight from the clipboard.

**Tags and stash shipped next**, closing out v1.1. Both confirmed the "budget a small tab, not
zero" caveat above: `RepoState` needed two new lists (`Tags`, `Stashes`), two new parsers, and a
new section embedded in an existing tab rather than a new one — Tags beside Branches, Stash
beside Changes. Stash also confirmed the "an action is atomic" assumption is worth defending
deliberately: `stash-pop` and `stash-apply` are only offered against a clean working tree, which
rules out landing on top of unsaved edits, but not a clash with commits made since the stash was
set aside — a clean tree does nothing to prevent that. v1.1 catches that case rather than
inventing a shape for it: it rolls the repository back to the clean state the precondition just
proved it was in, and leaves the stash in place.

### Bucket 2 — The one real architectural gap

**~~Merge and rebase~~ (both shipped).**

**Operation state and merge shipped ahead of v1.1**, on the reasoning below: it changes what
"an action" means, so building anything else against the old meaning means building it twice.
What it cost, against what this section predicted:

- **Operation state in `RepoState`** — as predicted, a nullable `OperationState`. Detection is
  `git rev-parse -q --verify MERGE_HEAD` rather than a `.git/MERGE_HEAD` probe, which is wrong
  in a linked worktree.
- **A persistent band** — as predicted, and it survives restart for free: state is read from
  the repository every refresh and never cached.
- **Actions that resume** — *not* as predicted. `merge-continue` and `merge-abort` are ordinary
  `GitAction` descriptors, and `GitAction`, `ActionService`, `ActionRequest` and the explain
  panel all took them unchanged. The misfit this section anticipated was about where the button
  lives, not about the shape of an action.

Two things this section did not anticipate:

- **A stopped merge is not a failure.** `git merge` exits non-zero on conflicts, which routed
  it to the error panel. `ActionOutcome.Paused` is derived from the observed operation
  transition rather than the exit code.
- **`git commit` mid-merge finalises the merge.** It succeeds, so nothing refused it — the
  Commit button would have ended a merge with a message written for something else. `commit`
  now carries `RequiresNoOperationInProgress`.

**Rebase shipped narrow**, and reused all of the above exactly as predicted: operation state,
the band, `Paused`, and resume actions as ordinary descriptors. What it added was the
sequencer — repeated stops, `--skip`, and step-of-total progress read from git's own sequencer
files, the first time this app reads them rather than asking git a question.

Two things it did not do. It does not rebase a branch that is already on a server: that needs
a force-push this app does not have, and offering it would strand the user behind a push
refusal whose translation would actively mislead them. And it did **not** force the `Danger`
enum open. Three levels still describe the gate correctly; what needed generalising was the
modal's consequence sentence, which was hardcoded as a switch in a viewmodel and now lives in
the content files beside every other word the user reads.

The entire flow assumes an action is **atomic**: preview → run → narrate → done. Merge and rebase
break that assumption. `git merge` can stop mid-operation and leave the repository in a state the
user must drive to completion or abandon. `git rebase` can stop repeatedly.

Before v2, `RepoState` had no concept of this. It modeled conflicts at the **file** level
(`ChangeKind.Unmerged`) but not at the **operation** level — there was no "a merge is in
progress", no `MERGE_HEAD` or rebase-sequencer awareness.

Closing that gap required:

- **Operation state in `RepoState`** — whether an operation is in flight, which one, and how far
  through. Shipped as the nullable `OperationState` described above.
- **A persistent UI band** — "you are in the middle of X: continue, or abort" — that survives
  closing and reopening the app, because the repository state does. Shipped, and read fresh on
  every refresh rather than cached.
- **Actions that resume rather than start** (`--continue`, `--abort`, `--skip`), which did not fit
  the original "an action is a thing you choose to do to a file or branch" shape. Shipped as
  ordinary `GitAction` descriptors — the misfit turned out to be about where the button lives,
  not about the shape of an action.

This was the load-bearing change. It is shared by merge and rebase, both now shipped, and by
cherry-pick-with-conflicts and guided conflict resolution, both still deferred. **Building it
before anything that depended on it, ahead of easier work that was available**, was the right
call: it changed what "an action" means, and building UI against the old meaning would have meant
building it twice.

### Bucket 3 — Not actions at all

**~~A diff viewer~~ (shipped).**

This is a read surface, and shipping it cost a content-model change this section did not
predict: `ExplanationDocument` carries a danger level and `what` / `risks` / `undo`, and a
surface the user only reads has none of the three. Bending it to fit would have meant three
empty sections and a danger level describing nothing, so content grew a fourth category
instead — `reading/`, alongside `actions/`, `terms/`, and `.gitignore` templates — holding a
`ReadingDocument` with just an id, a title, glossary terms, and `what`.

Conflicted files were left out on purpose, not for lack of time. `git diff` emits combined
diff format for unmerged paths — two `@@@`-marked hunks against two parents instead of one —
and `DiffParser` reads one grammar, not two. That surface belongs to v3, which is built for
choosing between sides rather than reading one; offering a diff button on a conflicted row here
would have opened onto an empty parse for the exact files a beginner most needs help with.

`git diff --no-index` exits non-zero whenever the files differ, which is every untracked file
compared against nothing. Judging that read by the exit code would report every new file as a
failure. This is the second time this codebase has been caught by an exit code that means
something other than failure — `ActionOutcome.Paused` was the first, at rebase — and the fix
carries the same discipline: `DiffReader` decides on the shape of `stdout`, never on `Success`,
and only falls back to the exit code when there is no output to shape a decision from.

The diff also could not live in `RepoState`. That snapshot is re-read on every action and every
file-watcher tick and has to stay cheap; a diff is per-file and its size has no bound. `DiffReader`
is a sibling of `RepoStateReader`, not a field on it, read on demand rather than kept — an open
diff is re-read inside the existing refresh, so what is on screen stays observed rather than
remembered, the same rule the rest of the app already lives by.

### Bucket 4 — Guided conflict resolution

Sits on Bucket 2 (operation state) plus Bucket 3 (diff rendering). Correctly the largest single
piece of work in the product.

It stays last not because it is least valuable — it is arguably the most valuable — but because
building it before its foundations exist would mean inventing operation state and diff rendering
badly, inside the most complex screen in the app.

---

## Declined, not deferred

These are not waiting for a later version. They are decisions to say no.

### Hunk-level staging

**Two independent reasons.**

*Technical:* it requires feeding a constructed patch to `git apply --cached` on stdin.
`GitRunner` has no stdin support, by design — every invocation is an argv array, never a
constructed string, never a shell. That single choke point is why quoting and injection defects
cannot occur in this codebase. Hunk staging is the one feature that would require loosening it,
and it is not worth the trade.

*Product:* a beginner staging half of a file is a beginner who does not yet understand what
staging is. It is an expert affordance in a tool whose entire premise is the absence of expertise.

If this is ever revisited, it should be revisited as "has the audience changed?" — not as "can we
fit it in?"

### Submodules

Submodules confuse experts. A tool built for people who do not know git has no business shipping
them, and a beginner who genuinely needs submodules needs a colleague, not a GUI.

---

## Sequence

| Version | Contents | Why here |
|---|---|---|
| **v1.1** | ~~Remote management, tags, stash~~ (all shipped) | No new concepts; proved the descriptor model scales past the original thirteen |
| **v2** | ~~Operation state, then merge~~ (shipped) | The load-bearing change everything below depends on |
| **v2.1** | ~~Rebase~~ (shipped) | Rode on v2's operation state; added the sequencer and history rewriting |
| **v2.5** | ~~Diff viewer~~ (shipped) | Independent of the above, and a prerequisite for v3 |
| **v3** | Guided conflict resolution — **next** | Sits on v2 + v2.5 |
| **—** | Hunk staging, submodules | Declined above |

**v2 shipped before v1.1.** The order in this table was the plan; the argument in Bucket 2 —
do the load-bearing change before anything that depends on it — won. v1.1's tags and stash plan
predates the `RepoState` shape v2 left behind; it was kept as a record rather than rewritten,
with a header saying so, because the features shipped and its exact-match anchors describe a
codebase that no longer exists.

---

## Known strain points

Two parts of the design were expected to come under pressure at v2. Merge has now been built,
so both can be reported on rather than predicted.

**The `Danger` enum is three-valued, and it survived rebase.** `discard-file`, `stash-drop`
and `rebase-skip` are now all `Destructive`, and the enum did not need a fourth level: what
had to change was where the modal's consequence sentence lives, not how danger is described.
The prediction was right that something would give at rebase, and wrong about what.

`merge-abort` is the closest call in the app —
it destroys hand-resolved conflict work unrecoverably — and it is still `Caution`, on the
grounds that promoting it without a reason drawn from an actual incident would be guessing at
a shape not yet visible, the same restraint this document has kept from the start. **This is
deferred, not resolved.** Force-push, if it is ever added, is the next thing that could force
the question; rebase, which this document expected to, did not.

**Narration snapshots repository state before and after, then describes the observed
difference.** This survived intact. `Narrator` gained three sentences for operations starting
and ending — rebase later took that to seven — and which one applies is decided by whether a
commit appeared, observed, not inferred from which action ran. The worry that partial
completion had no vocabulary turned out to be a vocabulary problem rather than a structural
one.

One strain point was **not** anticipated at merge, and rebase duly broke on it: `Success` on
`ActionOutcome` means the exit code, and an operation that pauses exits non-zero. `Paused`
patched this for merge by defining a pause as the *transition* into an operation — which is
indistinguishable from the right answer as long as the operation only ever stops once. A
rebase stops repeatedly, and `rebase --continue` that stops on the next commit's conflict
exits non-zero with the sequencer still in place, so under that definition it was not paused
and the user who had just fixed a conflict was shown an error for a command that did exactly
what was asked.

The resolution kept the discipline rather than reaching for the action id: a pause is now an
operation still in flight **after** the command, and — when one was already in flight — the
operation having visibly *moved*, by the sequencer counter or by HEAD. Both halves are
observed state. The second half is what stops an unrelated command that fails mid-operation
from having its real error swallowed. `Success` still means the exit code and nothing else;
what changed is the question asked alongside it.

---

## Adding an action today

For anything in Bucket 1, the path is short:

1. Add a `GitAction` descriptor to `ActionCatalog` — id, title, danger, argv builder,
   preconditions.
2. Add `src/GitHelper.Content/actions/<id>.md` with `what`, `risks`, and `undo` sections. The
   content id equals the action id by convention.
3. Reference any new glossary terms as `[[term-id]]`; add `src/GitHelper.Content/terms/<id>.md`
   if the term is new.
4. Wire a button to it in the relevant tab viewmodel.

No new UI code is needed for the explain, confirm, and narrate flow — that is what the descriptor
model buys.
