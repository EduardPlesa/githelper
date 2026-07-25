# GitHelper Avalonia UI — Design

**Date:** 2026-07-25
**Status:** Approved design, ready for implementation planning

## Purpose

This is Plan 2 of 2. It builds `GitHelper.App`, the Avalonia desktop UI that drives the
already-complete `GitHelper.Core` engine (spec:
`docs/superpowers/specs/2026-07-24-git-helper-design.md`, plan:
`docs/superpowers/plans/2026-07-24-githelper-core-engine.md`, merged via
[PR #2](https://github.com/EduardPlesa/githelper/pull/2)).

Every behavioral decision in the teaching flow — explain → confirm → run → narrate, danger
gating, content rendering, glossary hover, the command log — was already locked in during
Plan 1's design. This plan is about the concrete Avalonia realization of that behavior:
window structure, individual views, view-to-engine wiring, local settings, and packaging.
It introduces no new product behavior beyond what Plan 1 already specified, with three
small exceptions called out explicitly below (recent repositories, theme selection, and
the settings file that backs both).

## Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Avalonia version | 11.3.18 (11.x line) | Mature, best-documented line. Since this may be the first Avalonia app built here, community support outweighs being on the newest major version. |
| MVVM | `CommunityToolkit.Mvvm` | Already named in Plan 1's spec; source-generated `[ObservableProperty]`/`[RelayCommand]`. |
| Window layout | Three-pane: sidebar nav, center content, explain panel (right) | Chosen over a two-pane or single-column layout because it keeps the explain panel permanently visible with real width, rather than squeezed into a sidebar or a stacked accordion. |
| Repo opening | Startup screen only, with a recent-repositories list | The app always asks for a folder at startup rather than silently reopening the last one; a recents list makes that ask fast rather than requiring a fresh browse every time. No in-app repo switcher — closing and reopening the app is how you switch repos in v1. |
| Caution confirmation | Inline, in the explain panel | Consistent with the panel's existing four-heading structure; nothing ever covers the window for a reversible action. |
| Destructive confirmation | Modal dialog (the one exception) | `discard-file` is the only `Destructive` action in the whole v1 catalog. By the time a user reaches it, they have likely confirmed many `Caution` actions in the same on-screen spot — a modal breaks that reflex-click muscle memory by moving the confirmation to a different screen position entirely, for the one action that can permanently lose work. |
| Command log visibility | Small fixed-height strip, always visible | The log's entire purpose (per Plan 1) is teaching the CLI by being watched accumulate; a collapsed-by-default log undermines that. |
| Theme | Follows OS setting at launch, with a manual override toggle | Zero-configuration by default; the toggle is one small addition once `AppSettings` already exists for other reasons (see below). |

### Rejected alternatives

- **Two-pane layout with the explain panel taking over the content area.** Considered because
  it gives the explain panel more room when active, but it means the changes/history list
  disappears while reading an explanation, which works against a beginner cross-referencing
  what they clicked against what's listed.
- **Single-column guided/accordion layout.** Most novice-friendly in isolation, but doesn't
  scale to the History and Branches views without very different interaction patterns per
  view, and diverges most from the git GUIs (GitHub Desktop, Fork, GitKraken) a user may
  graduate to later — Plan 1's spec explicitly wants the command log to ease that transition,
  and a wildly different layout works against that goal.
- **Modal confirmation for every gated action, not just Destructive.** Rejected because it
  reintroduces exactly the popup-heavy feel the three-pane layout was chosen to avoid, for
  actions that are, by definition, safely reversible.
- **Remembering and silently reopening the last repository.** Rejected in favor of always
  asking — simpler to build and reason about, and the recents list closes the speed gap
  without hiding which repo is about to open.

## Architecture

### Project structure

Added to the existing solution:

| Project | Contents |
|---|---|
| `GitHelper.App` | Avalonia application: `App.axaml`, views, viewmodels, the content-block renderer, styles, `AppSettings`. References `GitHelper.Core` only — no engine logic is duplicated here. |
| `GitHelper.App.Tests` | `Avalonia.Headless` tests: viewmodel behavior (no window needed), and render-without-throwing / command-binding smoke tests for views. |

### Window structure

```
┌─────────────────────────────────────────────────────────┐
│ Top bar: repo name · branch selector · theme toggle       │
│          · Open Folder                                    │
├───────────┬───────────────────────────┬─────────────────┤
│  Sidebar  │      Center content        │  Explain panel  │
│  Changes  │  (swaps by sidebar tab)    │  (right, fixed  │
│  History  │                             │   width)        │
│  Branches │                             │                 │
├───────────┴───────────────────────────┴─────────────────┤
│  Command log strip (small, fixed height, always visible) │
└─────────────────────────────────────────────────────────┘
```

`MainWindow` hosts one `MainViewModel` owning: the current `RepoState` (refreshed via
`RepoStateReader` and a debounced `FileSystemWatcher`, per Plan 1's spec), the selected
sidebar tab, the `ExplainPanelViewModel`, and the `CommandLogViewModel`.

**Sidebar navigation** is a `ContentControl` whose content swaps between three
independently-testable view-models — `ChangesViewModel`, `HistoryViewModel`,
`BranchesViewModel` — selected by the sidebar tab. None of the three know about each
other; each only reads `RepoState` and calls `ActionService`.

**Explain panel** is always mounted, never a separate window, and has three states:

- **Empty** — a quiet placeholder ("Select a file or action to see what it does").
- **Preview** — populated from `ActionService.PreviewAsync`, rendering the four-heading
  structure from Plan 1 (What / Command / Risks / Undo) via the content renderer below.
- **Confirming** — the same content plus a Confirm button, inline for `Caution` actions.
  For the one `Destructive` action, this state instead opens `DiscardConfirmationDialog`
  as a modal rather than rendering inline.

**Command log strip** is a small fixed-height, scrollable list bound to
`CommandLog.Entries`, appended to live via `CommandLog.EntryRecorded`. Never collapsed.

### Startup and repository opening

The window chrome renders immediately on launch — the real three-pane shell, all panes
empty — with a centered overlay reading "Open a folder to get started". The overlay shows:

- An **Open Folder** button (native folder picker).
- A **recent repositories** list — up to 8 entries, most-recently-opened first, each
  showing the folder name and full path, click-to-open, with a small remove control for
  stale or deleted paths.

Choosing a folder (browse or recent-click) calls `RepoStateReader.FindRepoRootAsync`, then
`GitEnvironment.CheckAsync`:

- A `Blocking` result (git missing) replaces the overlay with that explanation instead of
  the picker.
- A `Warning` (identity unset) lets the app proceed into the three-pane view but surfaces
  the identity-setup prompt from Plan 1's spec.
- On success, the opened path moves to the top of the recents list (or is inserted if new)
  and the list is capped at 8, oldest dropped.

There is no in-app repository switcher in v1 — switching repos means closing and
reopening the app, which lands back on this same startup screen.

### The three views

**Changes view** (`ChangesViewModel`) — two sections mapping directly onto Plan 1's action
catalog:

- **Staged** — each row offers `unstage-file`; "Stage All" / "Unstage All" sit above each
  section; a commit message box and Commit button sit below Staged.
- **Unstaged & Untracked** — each row offers `stage-file` and `discard-file` (the
  modal-gated action).

Every button click flows through the same path: `ActionService.PreviewAsync` populates the
explain panel, then either an immediate run (`Safe`) or a wait for confirmation
(`Caution`/`Destructive`), then `RunAsync`, whose `Narrator` output is appended to a small
status line and to the command log.

**History view** (`HistoryViewModel`) — a list bound to `RepoState.RecentCommits`, each row
showing short hash, subject, author, and a relative date ("2 hours ago"). `undo-last-commit`
is offered only on the top row — consistent with `RequiresParentCommit` already refusing it
past the first commit.

**Branches view** (`BranchesViewModel`) — lists `RepoState.Branches` with upstream; offers
`create-branch`, `switch-branch`, `delete-branch` per row; shows detached-HEAD state plainly
when `RepoState.IsDetached` is true — the state Plan 1's final review specifically hardened
`push` against.

### Content rendering

The one piece of genuinely new UI logic, since Plan 1 only defined the data shape
(`ContentBlock`/`InlineSpan`), not its rendering. A small set of Avalonia controls maps 1:1
onto the closed block schema:

| Content type | Avalonia rendering |
|---|---|
| `ParagraphBlock` | `TextBlock` with inline runs |
| `BulletListBlock` | `ItemsControl` of bulleted `TextBlock`s |
| `CodeBlock` | Monospace `Border` + `TextBlock`, with a copy button |
| `TextSpan` | Plain `Run` |
| `CodeSpan` | Monospace `Run` |
| `TermSpan` | Underlined `Run`; a `ToolTip`/`Flyout` shows the `GlossaryTerm.Definition`, itself rendered through this same renderer, so a definition can contain its own paragraphs and bullets |
| `SlotSpan` | Never rendered directly — always pre-resolved to a `TextSpan` by `SlotBinder` before the document reaches the view |

This renderer is the one piece of UI logic worth unit-testing directly (via
`Avalonia.Headless`) rather than only through manual inspection: feed it a `ContentBlock`
tree, assert the resulting visual tree's text content.

### Local settings

Three small preferences accumulate across this plan — the per-action "stop explaining this
one" toggle (from Plan 1's spec), the recent-repositories list, and the theme override —
none large enough to justify a dedicated settings screen, but real enough to justify naming
as their own concern rather than scattering ad hoc file I/O across viewmodels.

`AppSettings`:

```csharp
public sealed record AppSettings(
    List<string> RecentRepositories,
    HashSet<string> SuppressedExplanations,
    ThemeVariant? ThemeOverride);
```

Persisted as JSON under `%LocalAppData%\GitHelper\settings.json`, loaded once at startup,
written on every change (a repo opened, an explanation suppressed, the theme toggled).
`ThemeOverride: null` means follow the OS setting; the manual toggle sets it explicitly.
There is no dedicated settings screen — each preference is set from wherever it naturally
lives (the recents list, the explain panel's "stop explaining this" checkbox, the top bar's
theme toggle).

### State refresh

A `FileSystemWatcher` on the repo root, debounced 500ms (per Plan 1's spec — the actual
performance lever, not framework choice), triggers `RepoStateReader.ReadAsync` on a
background thread; the result marshals to the UI thread via `Dispatcher.UIThread.Post` and
republishes through `MainViewModel`. Every view rerenders from the same `RepoState`
snapshot — no view owns independent state that could drift from another.

### Error handling

When `ActionOutcome.Success` is false and `Error` is populated, the explain panel switches
to an error state: `TranslatedError.Summary` / `Explanation` / `NextSteps` rendered plainly,
with `RawOutput` behind a collapsed "show technical details". This is exactly Plan 1's two
firm presentation rules (raw output always reachable, unknown errors admitted rather than
guessed) — no new error-UI concept is introduced; this plan only renders what
`ErrorTranslator` already produces.

## Testing

| Layer | Coverage |
|---|---|
| ViewModels | Headless — no Avalonia window at all. They depend only on `GitHelper.Core` interfaces (`ActionService`, `RepoStateReader`, `CommandLog`), so they're tested exactly like Plan 1's engine code: real git via `TestRepo`, no mocks. |
| Views | `Avalonia.Headless`: renders without throwing, clicking a bound button invokes the expected command. Not exhaustive visual testing. |
| Content renderer | Real unit tests: block tree in, visual tree text out (see Content rendering above). |
| Manual verification | Once buildable, the app is launched and driven through the golden path (open repo → stage → commit → push) and at least one edge case (empty repo, detached HEAD) in a real window — automated tests don't verify a UI "looks right." |

## Non-goals for this plan

Deliberately excluded, consistent with Plan 1's own non-goals list:

- No merge/rebase/stash/conflict UI — Plan 1 already deferred these to milestone 2 at the
  engine level; there is nothing for this UI plan to build against yet.
- No in-app repository switcher beyond the startup screen's recents list.
- No settings *screen* — the three preferences are each set in place, not through a
  dedicated settings surface.
- No theming beyond light/dark (no custom accent colors, no high-contrast mode).
