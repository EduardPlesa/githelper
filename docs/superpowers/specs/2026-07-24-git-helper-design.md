# GitHelper — Design

**Date:** 2026-07-24
**Status:** Approved design, ready for implementation planning

## Purpose

A desktop git GUI for people who do not know git. Every action explains, in plain
English, what it does before it runs, shows the exact git command it will execute,
warns about consequences in concrete terms, and describes how to undo it.

The app operates on the user's **real repositories**, not a simulator. A beginner
learns on their own work, with guardrails.

The secondary goal is that the user eventually outgrows the app: the command log
and the always-visible commands teach the CLI by exposure.

## Non-goals for v1

Deliberately excluded: merge, rebase, stash, tags, cherry-pick, conflict
resolution, remote management, submodules, and hunk-level staging.

Guided **conflict resolution is milestone 2**. It is the scariest part of git for a
beginner and the most complex UI in the product; it deserves its own design pass
rather than being squeezed into v1.

## Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Target | Real repositories with guardrails | A sandbox is discarded the moment real work starts. |
| Platform | Avalonia on .NET 10, native UI | .NET 10 already installed. Fast start, low memory, single language, no web build step. |
| Teaching model | Explain → confirm → narrate | Teaches at the moment of decision, which is when it sticks. |
| Git access | Spawn the real `git` binary | The product promises "this is the exact command." A reimplementation would make that promise false. |
| Explanations | Hand-written content files | Deterministic, offline, no API key, no per-click cost, and reviewable for correctness — which matters when teaching. |
| Scope | Daily loop + undo (13 actions) | Covers roughly 90% of beginner use without conflict/rebase complexity. |

### Rejected alternatives

- **isomorphic-git / libgit2 bindings** — would teach a git that is not the git on
  the user's machine.
- **Electron or WebView2 + React** — React requires a Chromium engine, which costs
  Chromium-class memory regardless of who ships it. Rejected in favour of native UI.
- **Live LLM-generated explanations** — latency on every click, cost, an API key
  requirement, and the possibility of being subtly wrong about git.

## Architecture

### Solution layout

| Project | Contents |
|---|---|
| `GitHelper.Core` | Class library. Git runner, output parsers, action descriptors, safety rules, error translator, content loader. **No Avalonia reference.** |
| `GitHelper.Content` | Authored explanation and glossary files, shipped as embedded resources. |
| `GitHelper.App` | Avalonia UI. Views and viewmodels, MVVM via `CommunityToolkit.Mvvm` source generators. |
| `GitHelper.Core.Tests` | xUnit. Parser fixtures, content checks, and real-git integration tests. |

`Core` having no UI dependency is the load-bearing constraint: the entire test
suite runs headless in milliseconds, and the UI layer can be replaced without
touching git or teaching logic.

### Git runner

The single choke point through which every git invocation passes.

- `Process` + `ProcessStartInfo.ArgumentList` — the argv-array equivalent. Never a
  joined command string, so quoting and injection defects cannot occur.
- stdout and stderr are read **concurrently and asynchronously**. Reading them
  sequentially deadlocks once output exceeds a pipe buffer; this is the classic
  .NET failure here and is designed out from the start.
- `StandardOutputEncoding = UTF8`, and every invocation carries
  `-c core.quotepath=false` so non-ASCII filenames are not mangled into octal
  escapes.
- Every call accepts a `CancellationToken` and returns
  `{ ArgVector, StdOut, StdErr, ExitCode, Duration }`.
- No invocation ever goes through a shell.

### Parsing

Repository state is read with `git status --porcelain=v2 -z --branch`.

The `-z` output is NUL-separated rather than line-based, and rename records carry
two paths within a single record. It is parsed by a record reader, not by line
splitting, and is tested against captured fixtures.

### Threading

Git runs off the UI thread; results marshal back via `async`/`await`. A slow
`push` never freezes the window.

### Packaging

```
dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishReadyToRun=true
```

One double-clickable `.exe`, no runtime installation required of the user.
Roughly 60–80 MB. ReadyToRun reduces cold start.

## Action model

Every action is **data, not a code path**:

```
GitAction {
  Id             // "commit", "discard-file", "undo-last-commit"
  BuildArgs()    // (state, params) -> string[]   — argv, never a string
  Preconditions  // checks evaluated against RepoState
  Danger         // Safe | Caution | Destructive
  ExplanationId  // -> content file
  UndoHint
}
```

Adding an action later is one descriptor plus one content file. No new UI code and
no new branches in the flow.

### The v1 action set

| Id | Command | Danger |
|---|---|---|
| `stage-file` | `git add -- <path>` | Safe |
| `unstage-file` | `git restore --staged -- <path>` | Safe |
| `stage-all` | `git add -A` | Safe |
| `unstage-all` | `git restore --staged -- .` | Safe |
| `commit` | `git commit -m <message>` | Caution |
| `create-branch` | `git switch -c <name>` | Safe |
| `switch-branch` | `git switch <name>` | Caution |
| `fetch` | `git fetch` | Safe |
| `pull` | `git pull --ff-only` | Caution |
| `push` | `git push` (or `git push --set-upstream origin <branch>`) | Caution |
| `discard-file` | `git restore -- <path>` | Destructive |
| `undo-last-commit` | `git reset --soft HEAD~1` | Caution |
| `delete-branch` | `git branch -d <name>` | Caution |

Two deliberate choices in this table:

- **`pull --ff-only`** rather than plain `pull`. A beginner should never have a
  merge commit appear that they did not ask for and cannot explain. When the pull
  cannot fast-forward, it refuses, and the error translator explains why in plain
  English. This is a teaching decision, not a technical one.
- **`branch -d`**, never `-D`. The safe form refuses to delete a branch holding
  unmerged work; that refusal is explained rather than overridden. Force-delete is
  out of scope for v1.
- **`create-branch` is `Safe` while `switch-branch` is `Caution`**, even though
  `switch -c` also switches. Creating a branch at the current HEAD carries the
  working changes along and cannot lose anything; switching to an *existing*
  branch can collide with uncommitted work. The danger levels reflect that real
  difference, not an oversight.

`discard-file` is the only `Destructive` action in v1. That is expected: v1 is
built so a beginner has very few ways to lose work.

### Flow

1. **Preview — nothing runs.** The UI calls `PreviewAsync(id, params)` and receives
   the exact argv that would execute, the explanation with live values substituted,
   concrete safety warnings, and the danger level.

2. **Explain panel — four headings, always in the same order**, so the structure
   becomes predictable rather than something to re-read each time:

   - **What this does** — plain English. Jargon terms are underlined with a hover
     definition rather than avoided, so the vocabulary is still taught.
   - **The command** — copyable.
   - **What could go wrong**
   - **How to undo this**

3. **Confirm — gated by danger level:**

   | Danger | Gate |
   |---|---|
   | `Safe` | Runs immediately; explanation shown alongside. |
   | `Caution` | Explicit confirmation. |
   | `Destructive` | Confirmation plus a consequence sentence containing **real numbers**: "permanently deletes your edits to 3 files — this cannot be undone". |

   A per-action **"stop explaining this one"** preference is stored in a settings
   file under the user's application data. Without it the app nags, and users begin
   reflex-clicking past explanations they have stopped reading — which defeats the
   entire purpose. `Destructive` confirmations are never suppressible.

4. **Run and narrate.** `RunAsync` **re-validates preconditions itself**; the
   viewmodel is not trusted, and state may have changed since the preview. It
   snapshots `RepoState` before and after, then narrates the **observed**
   difference rather than the intended one:

   > Created commit `a1b2c3d` with 3 files. `main` is now 1 commit ahead of `origin/main`.

   Narrating what actually happened means the app cannot confidently report success
   when git did something unexpected.

### Command log

A persistent pane listing every git command actually run during the session, with
exit codes, copyable.

This is the primary mechanism by which a user outgrows the app — the CLI is
absorbed by watching it accumulate. It is also the first thing wanted when
debugging the app itself.

### RepoState

A single immutable snapshot: current branch, upstream, ahead/behind counts, staged
/ unstaged / untracked file lists, recent commits, and a detached-HEAD flag. All
views render from it.

It is refreshed after every action and on a **debounced** `FileSystemWatcher`
(500 ms). This debounce is the real performance lever in the application — far
more than language or framework choice.

### Preconditions are where teaching happens

Rather than letting git fail cryptically, preconditions intercept and explain:

- **Push with no upstream** — explains what an upstream is, offers to set it.
- **Commit with nothing staged** — explains staging versus the working directory.
- **Switch branch with uncommitted changes** — explains the conflict and the options.
- **Commit with no configured identity** — see startup checks below.

## Content model

One Markdown file per action, with YAML frontmatter:

```yaml
---
id: commit
title: Commit
danger: caution
terms: [staging-area, commit, HEAD]
undo: undo-last-commit
---
## what
Saves a snapshot of the {stagedCount} file(s) you've staged onto branch {branch}...

## risks
...

## undo
...
```

Live values arrive through named slots — `{stagedCount}`, `{branch}`,
`{upstream}`, `{fileList}` — substituted from `RepoState` at render time.

Content is parsed into a **small, closed block schema** — paragraph, bullet list,
code block, inline code, and term reference — and rendered by hand-written
Avalonia controls.

`Markdown.Avalonia` was considered and rejected. Two reasons, the second decisive:
its current release targets Avalonia 11 and lags the framework, and it cannot
render the glossary term underline-and-hover behaviour, which is a core teaching
feature rather than a nicety. A general-purpose Markdown renderer cannot produce
the one thing this content most needs, so the renderer is written directly against
the block schema. Content authors still write plain text files, never XAML.

The parser accepts a deliberately small Markdown subset. Anything outside the
schema is a content error caught by tests, not silently dropped at runtime.

### Glossary

`terms/*.md`, one short definition per file, referenced by id. The renderer
underlines occurrences and shows the definition on hover.

Each term is defined exactly once, so correcting a poor explanation of "staging
area" corrects it everywhere it appears.

### Content correctness is enforced by tests

Content mistakes must be red tests, not blank panels at runtime:

- Every action id has a content file.
- Every `terms:` reference resolves to a glossary file.
- Every `{slot}` used is in the known slot vocabulary.
- Every `Destructive` action has a non-empty undo section.

## Error translation

Git's stderr is the single worst part of git for a beginner. An ordered rule set
maps `(stderr pattern, exit code)` to a cause, an explanation, and next steps.

Seed rules: rejected non-fast-forward push · no upstream configured · detached
HEAD · nothing to commit · authentication failure · not a git repository · local
changes would be overwritten · no remote configured · branch not fully merged.

Two presentation rules are firm:

1. **Raw stderr is always reachable** behind "show technical details", never
   hidden. A beginner who wants to search for the real message must be able to
   find it, and concealing it makes the app untrustworthy the first time someone
   notices.
2. **Unmatched errors are admitted, not guessed.** The raw output is shown with
   "I don't have a plain-English explanation for this one." A teaching tool that
   invents plausible git explanations is worse than one that admits ignorance.

## Credentials

**The app has no password field, ever.** It never collects, stores, or transmits
credentials.

Push and pull delegate authentication to Git Credential Manager, which ships with
Git for Windows. All network invocations set `GIT_TERMINAL_PROMPT=0` so git can
never block waiting on a prompt the user cannot see. Authentication failure
surfaces as a translated, explained message rather than a hang.

## Startup checks

- **git present on PATH** — if missing, a plain explanation and an install link.
- **git version** — recorded for diagnostics and shown in the command log header.
- **`user.name` and `user.email` configured** — an unconfigured identity makes a
  beginner's *very first commit* fail cryptically. The app detects this before that
  happens and offers to set it.

## Testing

All layers below run headless, since `Core` has no UI dependency.

| Layer | Coverage |
|---|---|
| Parsers | Fixture strings captured from real git output: renames, non-ASCII names, spaces in paths, detached HEAD, empty repo. |
| Content | The four checks listed under Content correctness. |
| Integration | A temp repo in a temp directory, **real git**, descriptors executed, resulting state asserted. No mocking — argv construction is exactly where defects live. |
| Safety | Each descriptor's declared `Danger` matches observed behaviour; destructive actions are gated. |

## Edge cases handled in v1

- **Empty repository with no commits.** Many commands behave differently; notably
  `git restore --staged` fails without a HEAD, so `unstage-file` and `unstage-all`
  fall back to `git rm --cached` in this state.
- **A repository with exactly one commit.** `undo-last-commit` uses `HEAD~1`,
  which does not resolve when there is no parent. The precondition detects this
  and explains that the first commit cannot be undone this way, rather than
  letting git fail with an ambiguous-argument error.
- **Detached HEAD** — detected, explained, and reflected in `RepoState`.
- **Repository with no remote** — push, pull, and fetch explain rather than fail.
- **Non-ASCII filenames and paths containing spaces** — covered by
  `core.quotepath=false`, argv arrays, and parser fixtures.

## Milestone 2

Guided merge and conflict resolution, designed separately.
