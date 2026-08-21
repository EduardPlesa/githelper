# Diff Viewer (v2.5) — Design

**Status:** shipped. This document has been brought back in line with what was built, so it
describes the thing that exists rather than the thing that was proposed.
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

## Rendering: git's own format, with the jargon explained beside it

The unified diff is shown as git prints it — added and removed lines, context, and the `@@`
hunk headers left **visible**.

The alternative was translating it — dropping `@@` in favour of "near line 42, 3 lines added".
That teaches a dialect only this app speaks. The README's stated goal is that the app make
itself obsolete, and a user who has learned to read a real diff can read one in any tool they
meet next. Jargon is not avoided here; it is explained where it stands.

**Every line carries git's own marker, in its own column.** A plus for an added line, a minus
for a removed one, a space for context, a backslash for the no-newline note. The marker is
**not** folded back into `DiffLine.Text`: that field is the line's own content, and v3's guided
conflict resolution has to be able to address a line and write it back. It is a computed
property on the model instead — the character git prints is domain data, not styling.

The marker is the primary signal and a translucent background tint is the second. Colour alone
would be invisible to a colour-blind reader, and `terms/diff.md` promises the user in so many
words that added lines "are marked with a plus, lines that were taken away with a minus". The
tints are low-alpha, like the grey the explain panel puts behind code, so a single pair of
colours reads correctly in both the light and the dark theme.

**The `@@` line is shown, but not underlined.** In this app an underline means "a glossary term
with a definition behind it", and `@@ -1,4 +1,6 @@` is a line of raw git output, not a term.
The definition of a hunk is reached the way every other definition in the app is: from the
authored prose above the diff, which underlines the word *hunk* and says which blocks are
hunks. It lives in `terms/hunk.md` and nowhere else.

**A short authored intro sits above the hunks**, rendered from `reading/file-changes.md`
through `ContentBlockRenderer`, exactly as `ExplainPanelView` renders its four sections. That
is what makes `diff`, `hunk` and the staging area hoverable here, and it is the reason the
`reading/` category exists at all — an intro written into the XAML would be a second copy of
prose the glossary already owns.

---

## The read layer

`DiffReader`, in `GitHelper.Core/Repo/`, a sibling of `RepoStateReader` and deliberately **not**
part of it.

`RepoState` is one immutable whole-repo snapshot, re-read on every action and every file-watcher
tick. Diffs are per-file and unbounded in size. Putting them in the snapshot would make the
cheapest and most frequent operation in the app the most expensive one. They are read on demand
instead, and never enter it.

Every invocation is prefixed `-c diff.suppressBlankEmpty=false`, then:

| Case | argv |
|---|---|
| Unstaged | `diff --no-color --no-ext-diff -- <path> [<old path>]` |
| Staged | `diff --no-color --no-ext-diff --cached -- <path> [<old path>]` |
| Untracked | `diff --no-color --no-ext-diff --no-index -- /dev/null <path>` |

Six choices fixed on purpose:

- **`--` before the path is mandatory.** Without it a file named like a ref makes git guess, and
  it guesses wrong.
- **`--no-color`**, because the parser reads text and not ANSI escapes.
- **`--no-ext-diff`**, because a user's configured external difftool would return a format this
  parser has never seen. The app reads git's own output or nothing.
- **`-c diff.suppressBlankEmpty=false`**, for the same reason. With that setting on, git prints
  a blank context line as a genuinely empty line rather than a single space. The parser must
  skip empty lines — `Split('\n')` manufactures one at the end of every diff — so the blank
  line would vanish and every line number after it in the hunk would come out one too low.
  Wrong line numbers are the one output this design must never produce. Unlike `GitRunner`'s
  hidden `core.quotepath`, this one stays in the visible argv: it changes the diff, so someone
  pasting the logged command should get back what the app showed them.
- **A renamed file passes both names.** git scopes rename detection to the paths it is handed:
  `diff --cached -- after.txt` reports a brand-new file with every line added, while
  `diff --cached -- after.txt before.txt` reports the rename git actually recorded. The old
  name comes from `FileChange.OriginalPath` in the snapshot and is threaded through
  `IDiffSource` into the pathspec. Never for `--no-index`, which takes exactly two paths.
- **Exit code is not the answer.** `--no-index` exits non-zero whenever the files differ, which
  is every untracked file. `Demand()` would throw on all of them. This is the same trap
  `ActionOutcome.Paused` hit at rebase: `DiffReader` decides on the shape of the output, never
  on `Success`.

**`/dev/null` was verified against a real repository on Windows** and holds, so no temporary
empty file is needed.

**Two things come free.** Every invocation already passes through `LoggingGitRunner`, so each
diff read appears in the command log as a real, copyable command — which serves the obsolescence
goal directly. And `SerializedGitRunner` already gates per repository, so a diff read cannot
collide with staging.

---

## The model and the parser

`DiffParser`, a static class with `Parse(string path, string output)`, matching `StatusParser`,
`LogParser`, `BranchParser`, `TagParser` and `StashParser`. The path is taken as well as the
output because `FileDiff` carries it and git's own header is not a reliable source for it —
`--no-index` names `/dev/null` on one side, and a rename names two different files. The view
renders a model; it never sees text.

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
    string Text,                // the line's own content, with no marker in it
    int? OldLineNumber,
    int? NewLineNumber)
{
    // The character git prints in column one for this kind: "+", "-", " " or "\".
    public string Marker { get; }
}
```

`Kind` is three-valued and `Truncated` is a separate bool. Truncation only ever qualifies a text
diff; folding it into the enum would present `Binary` and `TooLarge` as alternatives to each
other, which they are not.

**The header is kept verbatim as well as parsed.** The rendering decision needs the literal
string on screen. Parsing it into four integers as well is what gives every line its old and
new number, which is what v3 needs to offer "keep this side".

Line numbers are walked from each hunk header. An added line has no old number and a removed
line has no new one, and both are null rather than zero — the same reason `RebaseProgress` is
null for a merge instead of carrying `0 of 0`.

Seven rules, each a place a naive parser is quietly wrong:

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
  it would make the app's diff disagree with the terminal's. Its text starts at index 2, and
  that offset is guarded: `Parse` is public and documented as reading text nobody controls, so
  a line of just `\` must come out empty rather than throw.
- **Exactly at the cap is not over it.** Git's output ends with a newline, so `Split('\n')`
  hands the parser a trailing empty element. The cap is therefore checked immediately before a
  line is added, not at the top of the loop — otherwise that phantom element trips it and a
  diff shown in full is reported as cut off.

**Truncation** stops the parser after a named constant of 2000 lines and sets `Truncated`,
following `RepoStateReader.RecentCommitLimit` rather than burying a number in a method. The UI
then says so and names the command that shows the rest — **one command per side**, since the
three are different comparisons: `git diff -- <path>`, `git diff --cached -- <path>`, or
`git diff --no-index -- /dev/null <path>`. A single `git diff` would send a reader looking at
the staged diff to a different file state, and print nothing at all for an untracked file. The
sentence deliberately omits `--no-color` and `--no-ext-diff`: those exist to protect the parser,
and a person at a terminal wants neither. A truncated diff is the one place where "use the
terminal" is the honest answer, and this app is built to make itself unnecessary.

---

## The UI surface

Two facts from the existing code shape this. `FileChangeRowViewModel` already knows `IsStaged`,
and the same `FileChange` backs two rows when a file is staged and then edited again. And the
lists are `ItemsControl`, not `ListBox` — this app has no selection concept anywhere.

So **entry is a button per row**, not selection: "View changes", beside Stage, Unstage and
Discard, which is how every other affordance in that list already works. The button passes
`(path, side, originalPath)` from the row that owns it, so the two-rows-one-file case is
unambiguous by construction rather than by a rule someone must remember.

**Three sides, not two.** A row for a file git has never seen opens `--no-index` against the
empty side, which is a third comparison and gets its own label. Naming it "changes you have not
staged yet" would contradict the row that opened it, which calls the same file "new file".

**Ownership follows the existing wiring.** `MainViewModel` is documented as the only class that
knows how the pieces fit together, and no child viewmodel knows about its siblings.
`ChangesViewModel` therefore raises a request callback — exactly as `Startup.RepositoryOpenedAsync`
and `Explain.ActionCompletedAsync` do today — and `MainViewModel` swaps what the `ContentControl`
in the middle column shows. New `DiffViewModel` and `DiffView.axaml`. `MainTab` stays
three-valued: the diff is not a place you navigate to, it is a thing you open.

The surface shows the file path, a plain-English line naming which comparison this is — "Changes
you have not staged yet", "Changes you have staged, ready to commit", or "Every line in this new
file", never "worktree against index" — the authored intro, the hunks, and a way back. The
explain panel is untouched and stays on the right.

Leaving the Changes tab closes the diff. It is transient state about one file, and persisting it
would mean deciding what happens when that file stops existing.

### Rows that get no button

Two rows have no single diff to show, and both hide the button rather than let it fail.

**An untracked folder.** `git status` runs with the default `-u normal`, which collapses a
wholly untracked directory into a single entry whose path ends in `/`. There is no diff for a
folder: `diff --no-index -- /dev/null newdir/` exits 1 with `Could not access 'newdir/nul'` and
no output, which `DiffReader` correctly reports as a failed read. Offering a button whose only
possible outcome is "the changes in this file could not be read" is worse than offering none,
and making a folder of new files is among the first things a beginner does.

**A conflicted file**, for the reason below.

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
| Truncated | Showing the first 2000 lines, and the command for that side that shows the rest. |
| Empty | Names the side it was showing — one sentence per side, three in all: no unstaged changes in this file any more, no staged changes in it any more, or this file is no longer new to git. |
| File gone from disk | A sentence, not an error. |
| Read failed | Report it, keep the last diff, blank nothing. |

**Renamed with no content change is its own case, not "empty".** A staged rename produces a
rename header and zero hunks, which the parser reports as `DiffKind.Empty` — and "no changes any
more" would be a flat lie about a file that is plainly staged and plainly renamed. The row
already knows: `FileChange` carries `OriginalPath` and `ChangeKind.Renamed`. The surface takes
it from the snapshot, because the diff genuinely does not carry it.

`OriginalPath` also has to reach the **pathspec**, not just the sentence. Without it git never
prints a rename header at all — it reports a brand-new file with every line added, the surface
sees `DiffKind.Text`, and this whole case becomes unreachable. That is a bigger lie than the one
it was written to prevent, and it is why both names go into the argv.

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

- **`diff`** — a list of only the lines that changed, rather than the whole file twice. It is
  this definition that promises the reader a plus and a minus, which is why the marker column
  in the rendering above is not optional.
- **`hunk`** — what `@@ -1,4 +1,6 @@` means. The header stays visible, so the definition has to
  exist somewhere the reader can reach it.

Both are referenced from the new reading document, which is what keeps
`EveryGlossaryTermIsActuallyReferencedSomewhere` honest — that test walks `Actions` and `Setup`
today and must be extended to walk `Reading`, or the two new terms fail it. Named here because
it presents as a test failure at implementation time rather than as a design decision.

**The reading document has to be rendered, or none of this is true.** A `reading/` category that
`ContentLibrary` populates and no view reads is dead weight: the prose reaches nobody, the two
terms reach nobody, and the every-term-is-referenced rule is satisfied by a document that cannot
be opened — it stops proving what its name claims. `DiffViewModel` therefore takes the
`ContentLibrary` and exposes `Reading["file-changes"].What`, and `DiffView` renders it through
`ContentBlockRenderer` at the top of the surface. That is also what forbids a hand-written
paragraph or tooltip anywhere in `DiffView.axaml` restating a definition the glossary owns.

The reading document carries the one idea a beginner needs before `@@` means anything: a diff
shows changed lines, not the file. It has to earn that the way `rebase.md` earned replaying
commits.

---

## Testing

- **`DiffParser`**, on captured text, no repository needed: `@@ -1 +1 @@` with counts omitted;
  a file header whose `---` and `+++` must not read as removed and added; the no-newline marker,
  and a bare `\` that must not throw; both binary spellings; several hunks in one file; empty
  input; a diff of exactly `MaxLines` that is not truncated, and one over it that is. Old and
  new line numbers asserted per line, since that is what v3 leans on and what a reader cannot
  eyeball — and `Text` asserted clean beside `Marker`, since the marker living inside `Text` is
  precisely what v3 cannot tolerate.
- **`DiffReader`**, against real repositories through `TestRepo`: unstaged read; a file both
  staged and further modified giving two different diffs from one path, which is what the
  two-rows-one-file UI depends on; a staged rename read as a rename rather than a wholly new
  file; an untracked file arriving as all-added; line numbers surviving a repository with
  `diff.suppressBlankEmpty` set; and that `--no-index`'s non-zero exit is not read as failure.
  The last three are regressions this would otherwise ship, and none of them can be caught by a
  synthetic test — they are facts about git, so they need git.
- **`DiffViewModel`**, with a fake reader: opening publishes hunks; a refresh re-reads; a failed
  read keeps the previous diff and reports; the old name reaches the reader; empty (all three
  sides), binary, truncated (all three commands) and renamed-unchanged each produce their
  sentence; the intro comes from the content library.
- **`DiffView`**, headless: the markers reach the screen in order; added, removed and context
  get three different backgrounds; `Text` renders without a marker glued to it; the authored
  intro renders with its three terms underlined and tooltipped; and the `@@` line carries
  neither an underline nor a restated definition.
- **`MainViewModel`** swaps the tab content on request and restores it on back — asserted at the
  viewmodel level, as the operation band was. Also that a conflicted row and an untracked
  *folder* row both offer no button, while a loose untracked file still does.
- **Content integrity** gains the `Reading` category: its terms resolve, and the
  every-term-is-referenced rule walks it. Per this codebase's habit, that extension is watched
  failing before it is written, or it proves nothing.

Counts: actions stay at **29**, terms go **16 to 18**.

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
5. **A wholly untracked folder has no diff either.** `-u normal` collapses it into one row, and
   a folder is not a file. Showing the files inside it would mean asking git to list them and
   then reading N diffs, which is a different surface from "one file's changes".
