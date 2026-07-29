# GitHelper — Roadmap

**Status:** living document
**Last updated:** 2026-07-28

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

**Tags, stash, cherry-pick (clean), remote management.**

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

### Bucket 2 — The one real architectural gap

**Merge and rebase.**

The entire flow assumes an action is **atomic**: preview → run → narrate → done. Merge and rebase
break that assumption. `git merge` can stop mid-operation and leave the repository in a state the
user must drive to completion or abandon. `git rebase` can stop repeatedly.

Today `RepoState` has no concept of this. It models conflicts at the **file** level
(`ChangeKind.Unmerged`) but not at the **operation** level — there is no "a merge is in progress",
no `MERGE_HEAD` or rebase-sequencer awareness.

Closing this requires:

- **Operation state in `RepoState`** — is an operation in flight, which one, and how far through.
- **A persistent UI band** — "you are in the middle of X: continue, or abort" — that survives
  closing and reopening the app, because the repository state does.
- **Actions that resume rather than start** (`--continue`, `--abort`, `--skip`), which do not fit
  the current "an action is a thing you choose to do to a file or branch" shape.

This is the load-bearing change. It is shared by merge, rebase, cherry-pick-with-conflicts, and
guided conflict resolution. **Do it before anything that depends on it, even though easier work is
available**, because it changes what "an action" means — and building UI against the old meaning
means building it twice.

### Bucket 3 — Not actions at all

**A diff viewer.**

This is a read surface. It has no danger level, no preconditions, and no undo hint, so it does not
belong in the action catalogue. It needs new UI plus a parser for `git diff` output.

It is independent of everything else and is a prerequisite for conflict resolution.

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
| **v1.1** | ~~Remote management~~ (shipped), tags, stash | No new concepts; proves the descriptor model scales past the original thirteen |
| **v2** | **Operation state**, then merge and rebase | The load-bearing change everything below depends on |
| **v2.5** | Diff viewer | Independent of v2, and a prerequisite for v3 |
| **v3** | Guided conflict resolution | Sits on v2 + v2.5 |
| **—** | Hunk staging, submodules | Declined above |

---

## Known strain points

Two parts of the current design will come under pressure at v2. Neither is a defect today; both
are worth knowing before committing to that work.

**The `Danger` enum is three-valued, and `discard-file` is currently the only `Destructive`
action.** The modal is effectively shaped around one consequence sentence. Rebase — and force-push,
if it is ever added — would introduce destructive actions whose consequences have a quite different
shape. Expect the modal to need generalising, and expect that to be the moment the three-value
model is questioned.

**Narration snapshots repository state before and after, then describes the observed
difference.** That is elegant for atomic actions and awkward for pausable ones. "What happened"
for a merge that stopped halfway is a genuinely harder sentence to generate than "what happened"
for a commit, and the current `Narrator` has no vocabulary for partial completion.

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
