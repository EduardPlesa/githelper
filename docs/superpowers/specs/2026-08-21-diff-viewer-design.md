# Diff Viewer (v2.5) — Design

**Status:** approved, not yet planned
**Date:** 2026-08-21
**Builds on:** nothing structural. This is the first read surface that is not an action, and the
first time raw file content reaches the screen.

---

## What this is

One question: *"what did I actually change in this file?"* A file's unstaged changes (worktree
against index) and its staged changes (index against HEAD), shown as the unified diff git itself
prints, reached from the Changes tab.

## What this is not

- **Not an action.** No danger level, no preconditions, no undo hint, no entry in the catalogue.
  The action count stays at 29.
- **No commit or stash diffs.** History and the stash list keep their current summaries. The
  parser would serve them unchanged, and they can be added later without redesign.
- **No conflicted files.** See "Conflicts are v3's, not this plan's".
- **No staging from inside the viewer.** It reads; the lists act.
- **No side-by-side view.** The format git prints is the format worth learning.

### Why a diff viewer at all

The app explains what an action *will* do. It has never been able to show what the user *has
already done* — the Changes tab names files and their status, and stops there. "Modified" is not
an answer to "modified how", and a beginner deciding whether to stage a file is deciding blind.

It is also the prerequisite the roadmap named for v3 guided conflict resolution, which cannot be
built without line-level structure to address.

---

## Rendering: git's own format, with the jargon underlined

The unified diff is shown as git prints it — added and removed lines, context, and the `@@`
hunk headers left **visible**, underlined, with a hover definition. The same treatment prose
gets today.

The alternative was translating it — dropping `@@` in favour of "near line 42, 3 lines added".
That teaches a dialect only this app speaks. The README's stated goal is that the app make
itself obsolete, and a user who has learned to read a real diff can read one in any tool they
meet next. Jargon is not avoided here; it is explained where it stands.

---

## The read layer

`DiffReader`, in `GitHelper.Core/Repo/`, a sibling of `RepoStateReader` and deliberately **not**
part of it.

`RepoState` is one immutable whole-repo snapshot, re-read on every action and every file-watcher
tick. Diffs are per-file and unbounded in size. Putting them in the snapshot would make the
cheapest and most frequent operation in the app the most expensive one. They are read on demand
instead, and never enter it.

| Case | argv |
|---|---|
| Unstaged | `diff --no-color --no-ext-diff -- <path>` |
| Staged | `diff --no-color --no-ext-diff --cached -- <path>` |
| Untracked | `diff --no-color --no-ext-diff --no-index -- <empty> <path>` |

Four choices fixed on purpose:

- **`--` before the path is mandatory.** Without it a file named like a ref makes git guess, and
  it guesses wrong.
- **`--no-color`**, because the parser reads text and not ANSI escapes.
- **`--no-ext-diff`**, because a user's configured external difftool would return a format this
  parser has never seen. The app reads git's own output or nothing.
- **Exit code is not the answer.** `--no-index` exits non-zero whenever the files differ, which
  is every untracked file. `Demand()` would throw on all of them. This is the same trap
  `ActionOutcome.Paused` hit at rebase: `DiffReader` decides on the shape of the output, never
  on `Success`.

**To verify against a real repository before the plan records it:** how the empty side of
`--no-index` is spelled portably on Windows. `/dev/null` is git's own convention and Git for
Windows generally honours it, but the rebase spec verified every sequencer fact against a live
paused rebase before writing it down, and this is held to the same bar. If it does not hold, the
fallback is a temporary empty file — which costs a lifetime to manage and is only worth paying
if the first option genuinely fails.

**Two things come free.** Every invocation already passes through `LoggingGitRunner`, so each
diff read appears in the command log as a real, copyable command — which serves the obsolescence
goal directly. And `SerializedGitRunner` already gates per repository, so a diff read cannot
collide with staging.

---

## The model and the parser

`DiffParser`, a static class with `Parse(string output)`, matching `StatusParser`, `LogParser`,
`BranchParser`, `TagParser` and `StashParser`. The view renders a model; it never sees text.

```csharp
public sealed record FileDiff(
    string Path,
    DiffKind Kind,              // Text, Binary, Empty
    IReadOnlyList<DiffHunk> Hunks,
    bool Truncated);

public sealed record DiffHunk(
    string Header,              // the raw "@@ -1,4 +1,6 @@ ..." line, verbatim
    int OldStart, int OldCount,
    int NewStart, int NewCount,
    IReadOnlyList<DiffLine> Lines);

public sealed record DiffLine(
    DiffLineKind Kind,          // Context, Added, Removed, NoNewlineMarker
    string Text,
    int? OldLineNumber,
    int? NewLineNumber);
```

`Kind` is three-valued and `Truncated` is a separate bool. Truncation only ever qualifies a text
diff; folding it into the enum would present `Binary` and `TooLarge` as alternatives to each
other, which they are not.

**The header is kept verbatim as well as parsed.** The rendering decision needs the literal
string on screen to underline. Parsing it into four integers as well is what gives every line
its old and new number, which is what v3 needs to offer "keep this side".

Line numbers are walked from each hunk header. An added line has no old number and a removed
line has no new one, and both are null rather than zero — the same reason `RebaseProgress` is
null for a merge instead of carrying `0 of 0`.

Five rules, each a place a naive parser is quietly wrong:

- **`@@ -1 +1 @@` is legal.** Omitted counts default to 1. Requiring the comma silently drops
  every single-line hunk.
- **File headers are skipped** — `diff --git`, `index`, `---`, `+++`, `old mode`, `rename from`.
  Note that `---` and `+++` would read as a removed and an added line under a
  colour-by-first-character rule, which is one of the reasons there is a parser at all.
- **Binary arrives as prose** — `Binary files a/x and b/x differ`, or `GIT binary patch` when a
  user's config asks for it. Both mean `DiffKind.Binary`.
- **Empty output means no differences**, not an error. It happens legitimately when a file is
  edited and reverted between the snapshot and the read.
- **`\ No newline at end of file` is its own line kind**, not context. Git prints it, and hiding
  it would make the app's diff disagree with the terminal's.

**Truncation** stops the parser after a named constant of 2000 lines and sets `Truncated`,
following `RepoStateReader.RecentCommitLimit` rather than burying a number in a method. The UI
then says so and names the command that shows the rest. A truncated diff is the one place where
"use the terminal" is the honest answer, and this app is built to make itself unnecessary.

---

## The UI surface

Two facts from the existing code shape this. `FileChangeRowViewModel` already knows `IsStaged`,
and the same `FileChange` backs two rows when a file is staged and then edited again. And the
lists are `ItemsControl`, not `ListBox` — this app has no selection concept anywhere.

So **entry is a button per row**, not selection: "View changes", beside Stage, Unstage and
Discard, which is how every other affordance in that list already works. The button passes
`(path, staged)` from the row that owns it, so the two-rows-one-file case is unambiguous by
construction rather than by a rule someone must remember.

**Ownership follows the existing wiring.** `MainViewModel` is documented as the only class that
knows how the pieces fit together, and no child viewmodel knows about its siblings.
`ChangesViewModel` therefore raises a request callback — exactly as `Startup.RepositoryOpenedAsync`
and `Explain.ActionCompletedAsync` do today — and `MainViewModel` swaps what the `ContentControl`
in the middle column shows. New `DiffViewModel` and `DiffView.axaml`. `MainTab` stays
three-valued: the diff is not a place you navigate to, it is a thing you open.

The surface shows the file path, a plain-English line naming which comparison this is — "Changes
you have not staged yet", or "Changes you have staged, ready to commit", never "worktree against
index" — the hunks, and a way back. The explain panel is untouched and stays on the right.

Leaving the Changes tab closes the diff. It is transient state about one file, and persisting it
would mean deciding what happens when that file stops existing.

### Conflicts are v3's, not this plan's

`git diff` on an unmerged path emits **combined diff** format — `@@@ -1,4 -1,4 +1,6 @@@`, two
parents, a different grammar from the one parsed above. Conflicted files are exactly what v3
exists to handle, on a surface built for choosing between sides rather than reading one.

Conflicted rows therefore get no View button here, and v3 adds the second grammar deliberately
instead of this plan half-supporting it.

---

## Freshness

The diff re-read happens **inside `RefreshAsync`**, after the snapshot publishes, and only while
a diff surface is open. Inside the existing refresh gate, on the same cancellation token: part
of the refresh, not a race beside it.

Three behaviours it inherits by sitting there, each of which would otherwise be reinvented:

- **A failed read says so and changes nothing.** `RefreshAsync` already refuses to publish a
  blank state on `GitReadException`, on the grounds that work appearing to have vanished is the
  most alarming possible lie. A failed diff read keeps the last diff on screen and reports.
- **Serialization is free**, from `SerializedGitRunner`.
- **The command log stays consistent.** Every refresh already logs seven-odd read commands, and
  the log is documented as never hidden or filtered. One `git diff` joins them rather than
  becoming an exception to that rule.

**When the diff comes back empty, the surface stays put and says so.** A file can stop having
unstaged changes while it is open — an editor reverts it, or something else stages it. The
surface then says so, naming the side it was showing rather than saying "no changes" flatly, and
the user leaves by choosing to. It never navigates itself: surprises in this app are narrated, not performed.

**The honest cost:** while a diff is open, every watcher tick spends one extra `git diff` on a
file the user may have finished reading. Bounded by the truncation cap, and it buys the
guarantee that what is on screen is observed state rather than a remembered one — the discipline
that made the rebase narration trustworthy.

---

## Errors and edge cases

Each gets a plain sentence on the surface. Never a dialog, never a raw git message.

| Case | What the surface says |
|---|---|
| Binary | Not a text file, so there are no lines to compare. Git can tell it changed, not how. |
| Truncated | Showing the first 2000 lines, and the command that shows the rest. |
| Empty | Names the side it was showing: no unstaged changes in this file any more, or no staged changes in it any more. |
| File gone from disk | A sentence, not an error. |
| Read failed | Report it, keep the last diff, blank nothing. |

**Renamed with no content change is its own case, not "empty".** A staged rename produces a
rename header and zero hunks, which the parser reports as `DiffKind.Empty` — and "no changes any
more" would be a flat lie about a file that is plainly staged and plainly renamed. The row
already knows: `FileChange` carries `OriginalPath` and `ChangeKind.Renamed`. The surface takes
it from the snapshot, because the diff genuinely does not carry it.

**Long lines scroll horizontally; they do not wrap.** Wrapping breaks the correspondence between
screen rows and line numbers, and a diff whose numbers stop matching the file is worse than one
that has to be scrolled.

**No new `ErrorTranslator` rules.** Its rules are pattern-matched and scoped to action ids; a
diff read is not an action. Inventing translations for failures nobody has seen would be
guessing at a shape not yet visible — the restraint the roadmap keeps over `merge-abort`.

---

## Content

The content library already has three categories, not two: `actions/`, `terms/`, and `setup/`,
the last being explanation documents that are not actions. So prose that is not an action has a
precedent.

But `setup` documents are full `ExplanationDocument`s, with `danger`, `## what`, `## risks` and
`## undo`. A read surface has no danger, no risks and nothing to undo. Forcing it into that
shape ships three empty sections and a `danger: safe` that describes nothing — the same
meaningless-zeros problem the rebase spec refused when it made `RebaseProgress` null for a merge.

So a **fourth category**, `reading/`, with its own small document type: id, title, one body
section, and a `terms:` list. `ContentLibrary` gains a `Reading` dictionary alongside the other
three. Wildcard pickup means no manual registration.

**Two new terms**, taking the count 16 to 18:

- **`diff`** — a list of only the lines that changed, rather than the whole file twice.
- **`hunk`** — what `@@ -1,4 +1,6 @@` means. The rendering decision depends on this one: the
  header stays visible and underlined, so the definition has to exist to hover.

Both are referenced from the new reading document, which is what keeps
`EveryGlossaryTermIsActuallyReferencedSomewhere` honest — that test walks `Actions` and `Setup`
today and must be extended to walk `Reading`, or the two new terms fail it. Named here because
it presents as a test failure at implementation time rather than as a design decision.

The reading document carries the one idea a beginner needs before `@@` means anything: a diff
shows changed lines, not the file. It has to earn that the way `rebase.md` earned replaying
commits.

---

## Testing

- **`DiffParser`**, on captured text, no repository needed: `@@ -1 +1 @@` with counts omitted;
  a file header whose `---` and `+++` must not read as removed and added; the no-newline marker;
  both binary spellings; several hunks in one file; empty input. Old and new line numbers
  asserted per line, since that is what v3 leans on and what a reader cannot eyeball.
- **`DiffReader`**, against real repositories through `TestRepo`: unstaged read; staged read; a
  file both staged and further modified giving two different diffs from one path, which is what
  the two-rows-one-file UI depends on; an untracked file arriving as all-added; and that
  `--no-index`'s non-zero exit is not read as failure. That last is the regression this would
  otherwise ship.
- **`DiffViewModel`**, with a fake reader: opening publishes hunks; a refresh re-reads; a failed
  read keeps the previous diff and reports; empty, binary, truncated and renamed-unchanged each
  produce their sentence.
- **`MainViewModel`** swaps the tab content on request and restores it on back — asserted at the
  viewmodel level, as the operation band was.
- **Content integrity** gains the `Reading` category: its terms resolve, and the
  every-term-is-referenced rule walks it. Per this codebase's habit, that extension is watched
  failing before it is written, or it proves nothing.

Counts: actions stay at **29**, terms go **16 to 18**. The suite total goes into the plan once
the tests exist, not before.

---

## Known limitations, stated rather than discovered

1. **Conflicted files have no diff.** Combined diff format is a second grammar, and the surface
   that needs it is v3's.
2. **Non-UTF-8 files show replacement characters.** `GitRunner` decodes stdout as UTF-8
   unconditionally. That has cost nothing so far, because commit subjects and branch names are
   effectively always UTF-8 — a diff is the first surface where raw file content reaches the
   screen. Fixing it means encoding negotiation git cannot reliably help with, which is a
   subsystem rather than a section of this plan.
3. **Diffs over 2000 lines are cut off**, with the real command offered instead.
4. **Commits and stashes still have no diff.** The parser would serve both unchanged; only the
   entry points are missing.
