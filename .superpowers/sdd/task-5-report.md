# Task 5 Report: Slot resolution, confirmation abstraction, explain panel viewmodel

## What was implemented

Exactly per the brief, verbatim:

- `src/GitHelper.App/Content/SlotResolver.cs` — pure static `Resolve` that walks
  `ParagraphBlock`/`BulletListBlock`/`CodeBlock`, replacing every `SlotSpan` with a bound
  `TextSpan`, throwing `InvalidOperationException` (naming the slot) for an unbound slot.
- `src/GitHelper.App/Infrastructure/IConfirmationDialog.cs` — the modal seam:
  `Task<bool> ConfirmDestructiveAsync(string title, string consequence, CancellationToken ct = default)`.
- `src/GitHelper.App/ViewModels/ViewModelBase.cs` — `abstract class ViewModelBase : ObservableObject`.
- `src/GitHelper.App/ViewModels/ExplainPanelViewModel.cs` — the panel viewmodel: `ExplainPanelState`
  enum (`Empty`/`Explaining`/`Error`), observable properties (`PanelState`, `Title`, `CommandLine`,
  `DangerLevel`, `WhatBlocks`, `RisksBlocks`, `UndoBlocks`, `Blockers`, `CanRun`,
  `RequiresConfirmation`, `Narration`, `Error`, `ShowTechnicalDetails`,
  `SuppressExplanationForThisAction`), plus `ShouldRunImmediately`, `ActionCompleted` event,
  `ShowAsync`, `RunAsync`, `ShowAndRunIfUngatedAsync`, `Clear`.
- `tests/GitHelper.App.Tests/TestDoubles.cs` — appended `StubConfirmationDialog` (did not touch
  the three existing doubles).
- `tests/GitHelper.App.Tests/SlotResolverTests.cs` (5 tests) and
  `tests/GitHelper.App.Tests/ExplainPanelViewModelTests.cs` (16 tests), both copied from the
  brief verbatim.

The danger-gating rule lives entirely in `ComputeRequiresConfirmation`: `Safe` → false,
`Destructive` → always true (no settings lookup — literally cannot be suppressed),
`Caution` → true unless `SuppressedExplanations` contains the action id. `RunAsync` only calls
`_confirmations.ConfirmDestructiveAsync` when `DangerLevel == Danger.Destructive`, and only
persists a suppression when `SuppressExplanationForThisAction && DangerLevel != Danger.Destructive`.

## TDD evidence

**Step 2 — failing before implementation** (`dotnet test tests/GitHelper.App.Tests/GitHelper.App.Tests.csproj --filter "SlotResolverTests|ExplainPanelViewModelTests"`, before any production code existed):

```
error CS0234: The type or namespace name 'ViewModels' does not exist in the namespace 'GitHelper.App'
error CS0234: The type or namespace name 'Content' does not exist in the namespace 'GitHelper.App'
error CS0246: The type or namespace name 'ExplainPanelViewModel' could not be found
error CS0246: The type or namespace name 'StubConfirmationDialog' could not be found
```

Failed for the expected reason (types don't exist yet) — a compile error, not a runtime failure.

**Step 7 — passing after implementation** (same filter, after all four production files and the
test-double addition):

```
Passed!  - Failed:     0, Passed:    21, Skipped:     0, Total:    21, Duration: 10 s - GitHelper.App.Tests.dll (net10.0)
```

`RunAsync_SwitchesToTheErrorStateAndKeepsRawOutputReachable` (the unreachable-remote push test)
ran inside that 10s window without hanging — git gave up on `https://example.invalid/nope.git`
in a few seconds as expected.

**Step 8 — whole suite**:

```
Passed!  - Failed:     0, Passed:   138, Skipped:     0, Total:   138, Duration: 7 s  - GitHelper.Core.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    52, Skipped:     0, Total:    52, Duration: 12 s - GitHelper.App.Tests.dll (net10.0)
```

138 + 52 = 190, matching the expected total (169 prior + 21 new). No regressions.

**Warnings check**: explicit `dotnet build` of both `GitHelper.App.csproj` and
`GitHelper.App.Tests.csproj` after the change reports `0 Warning(s), 0 Error(s)`. Both projects
have `TreatWarningsAsErrors=true`, so a build success already implied this, but confirmed directly.

## Files changed

- Created: `src/GitHelper.App/Content/SlotResolver.cs`
- Created: `src/GitHelper.App/Infrastructure/IConfirmationDialog.cs`
- Created: `src/GitHelper.App/ViewModels/ViewModelBase.cs`
- Created: `src/GitHelper.App/ViewModels/ExplainPanelViewModel.cs`
- Modified: `tests/GitHelper.App.Tests/TestDoubles.cs` (appended `StubConfirmationDialog`)
- Created: `tests/GitHelper.App.Tests/SlotResolverTests.cs`
- Created: `tests/GitHelper.App.Tests/ExplainPanelViewModelTests.cs`

## Self-review

- **Destructive confirmation genuinely unsuppressible?** Yes.
  `ComputeRequiresConfirmation` returns `true` for `Danger.Destructive` unconditionally, never
  consulting `_settings`. `DestructiveConfirmationCanNeverBeSuppressed` (settings pre-suppressed
  for `discard-file`) still asserts `RequiresConfirmation == true` and
  `ShouldRunImmediately == false`. Confirmed passing.
- **Declining leaves the file untouched, `RunAsync` returns false?** Yes.
  `RunAsync_ConsultsTheModalForTheDestructiveActionAndHonoursCancel` sets `NextAnswer = false`,
  asserts `ran == false`, `CallCount == 1`, consequence mentions the real filename, and the
  file content is still `"vandalised\n"` — nothing ran because `RunAsync` returns immediately
  after `if (!confirmed) return false;`, before calling `_actions.RunAsync`.
- **`RunAsync` consults the dialog for `discard-file`, never for `commit`?** Yes — gated purely
  on `DangerLevel == Danger.Destructive`; `RunAsync_NeverConsultsTheModalForANonDestructiveAction`
  asserts `CallCount == 0` after running a commit.
- **`ShowAsync` resolves every slot?** Yes — `WhatBlocks`/`RisksBlocks`/`UndoBlocks` are all
  passed through `SlotResolver.Resolve` before being assigned;
  `ShowAsync_ResolvesSlotsSoNoSlotSpanSurvives` walks every span in all three collections and
  asserts none is a `SlotSpan`.
- **Git failure → error state with `RawOutput` populated, `ShowTechnicalDetails` still false?**
  Yes — on `!outcome.Success`, `Error = outcome.Error`, `ShowTechnicalDetails = false` explicitly
  (redundant with the default but kept for clarity/symmetry with the success branch), and
  `PanelState = ExplainPanelState.Error`. Confirmed by the unreachable-remote test.
- **Suppression toggle persists only for non-Destructive?** Yes, guarded explicitly in code
  (`SuppressExplanationForThisAction && DangerLevel != Danger.Destructive`) even though the
  brief's test suite doesn't exercise the destructive side of that guard directly — the
  guard exists and is straightforward to verify by inspection.
- **Test output pristine?** Yes — 0 warnings on explicit rebuild of both affected projects.

No deviations from the brief's exact source or tests were needed; the engine types
(`ActionService`, `ActionRequest`, `ActionPreview`, `ActionOutcome`, `Danger`, `TranslatedError`,
`ContentLibrary`, `SlotBinder`, `GitRunner`, `RepoStateReader`, the `ContentBlock`/`InlineSpan`
family), `ISettingsStore`/`AppSettings`, and `TestRepo` all matched the brief's assumptions
exactly (verified by reading each source file before writing code), so no naming traps or
signature mismatches surfaced beyond the two the brief called out (`DangerLevel` vs `Danger`,
and the fully-qualified `GitHelper.App.Content.SlotResolver` call).

## Concerns

None. All 21 new tests pass, the full suite reaches 190 with no regressions, and both
danger-gating naming traps from the brief were followed as specified.

## Fix: destructive-suppression coverage

**Problem:** The guard at `ExplainPanelViewModel.cs:130` prevents persisting suppression for 
destructive actions (`DangerLevel != Danger.Destructive`). This is correct and critical — 
silencing the confirmation on a permanently-destructive action must be impossible. However, 
no test covered the destructive side of the guard. A refactor inverting `!=` to `==` would 
break the guard but all tests would pass.

**Test added:** `SuppressExplanationForThisAction_IsNeverPersistedForADestructiveAction` in 
`tests/GitHelper.App.Tests/ExplainPanelViewModelTests.cs` (lines 233–246). Creates a 
`TestRepo`, modifies `README.md`, sets `confirmations.NextAnswer = true` to allow the action, 
shows `discard-file`, sets `SuppressExplanationForThisAction = true`, runs, and asserts 
`SaveCount == 0` and `discard-file` not in `SuppressedExplanations`.

**Mutation check:**
- Guard inverted to `DangerLevel == Danger.Destructive`: test **FAILS** with 
  `Assert.DoesNotContain() Failure: Item found in set ["discard-file"] Found: "discard-file"`
- Guard restored to `DangerLevel != Danger.Destructive`: test **PASSES**

**Test results:**
- ExplainPanelViewModelTests: 17 passing (16 existing + 1 new)
- Full suite: 191 passing (190 existing + 1 new)

Commit: `9156d7e` — "test: add destructive-suppression coverage"
