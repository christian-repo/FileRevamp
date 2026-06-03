---
plan: "02-02"
phase: "02-safety-and-reporting"
status: complete
started: "2026-06-03"
completed: "2026-06-03"
requirements: [RPRT-03, UX-01]
key-decisions:
  - "Test LiveRun_FailingRename uses --replace 'csv->' to produce trailing-dot filename, not '_new.csv' removal, because Path.GetFileNameWithoutExtension('.csv') returns '.csv' on Windows (non-empty stem)"
  - "Collision test uses '_v{*}_' wildcard pattern that strips including both underscores, producing 'prefixsuffix.csv' not 'prefix_suffix.csv'"
  - "Cherry-picked plan 02-01 commits (024cf06, 509c378) into worktree branch; applied orchestrator refactor via git show to avoid RenameCommand conflict"
key-files:
  created:
    - src/FileRevamp/Output/FailureLogger.cs
    - tests/FileRevamp.Tests/Output/FailureLoggerTests.cs
  modified:
    - src/FileRevamp/Commands/RenameCommand.cs
    - src/FileRevamp/Core/RenameOrchestrator.cs
    - src/FileRevamp/Program.cs
    - tests/FileRevamp.Tests/Commands/RenameCommandTests.cs
tech-stack:
  added: []
  patterns:
    - "Lazy file creation via BCL File.AppendAllText (creates file on first call if absent)"
    - "Two-pass orchestrator wired through command: Plan() + Execute() + earlyResults.Concat()"
    - "Log-file exclusion filter with StringComparison.OrdinalIgnoreCase before Plan()"
    - "TDD RED/GREEN for both FailureLogger (5 tests) and RenameCommand Phase 2 changes (3 new + 1 extended)"
dependency-graph:
  requires: [02-01]
  provides: [RPRT-03, UX-01]
  affects:
    - src/FileRevamp/Commands/RenameCommand.cs
    - src/FileRevamp/Output/FailureLogger.cs
metrics:
  duration: "34m"
  tasks_completed: 2
  tasks_total: 2
  files_created: 2
  files_modified: 4
  tests_added: 8
  test_suite_count: 66
  completed_date: "2026-06-03"
---

# Phase 2 Plan 02: FailureLogger, Two-Pass Wiring, Log Exclusion, and Help Examples Summary

**One-liner:** Lazy-write FailureLogger writes timestamped FAIL lines to rename-failures.log; RenameCommand wired to two-pass Plan()+Execute() with log-file exclusion and FailureLogger call for every Failed result.

## What Was Built

### Task 1: FailureLogger class with unit tests (TDD)

**`src/FileRevamp/Output/FailureLogger.cs`** — Sealed class in `FileRevamp.Output` namespace.

- Constructor takes `string directoryPath`; stores `_logFilePath = Path.Combine(directoryPath, "rename-failures.log")`
- `LogFilePath` property exposes the full path for use in exclusion filters
- `Log(string originalName, string reason)` appends one line per call: `[YYYY-MM-DD HH:mm:ss] FAIL {name}: {reason}`
- **Lazy creation:** uses `File.AppendAllText` which creates the file only on the first `Log()` call; no empty log files on successful runs (RPRT-03)
- Does NOT inject `IFileSystem` — failure log is a diagnostic output, always written to real disk even in dry-run mode

**`tests/FileRevamp.Tests/Output/FailureLoggerTests.cs`** — 5 unit tests covering:
- File created with FAIL-prefixed line on first `Log()` call
- File NOT created when `Log()` is never called (lazy creation)
- Multiple calls append all lines
- `LogFilePath` returns correct path
- Line format matches `\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\] FAIL {name}: {reason}`

### Task 2: Two-pass orchestrator wiring, log exclusion, failure logging, help examples (TDD)

**`src/FileRevamp/Commands/RenameCommand.cs`** — Three targeted additions:

1. **Log-file exclusion filter (RPRT-03, Pitfall 3):** After `FileDiscovery.GetFiles()`, filters out `rename-failures.log` (case-insensitive `OrdinalIgnoreCase`) before calling `Plan()`. Prevents the log file from entering the rename batch on repeated runs.

2. **Two-pass orchestrator call (SAFE-01, SAFE-02):** Replaces the old single-pass `Execute()` call with `Plan(filePaths, ...) → Execute(proposals, ...) → earlyResults.Concat(executeResults)`.

3. **FailureLogger wiring (RPRT-03):** After collecting all results, iterates `results.Where(r => r.Status == RenameStatus.Failed)` and calls `failureLogger.Log(result.OriginalName, result.FailureReason ?? "Unknown error")`.

**`src/FileRevamp/Program.cs`** — Added `config.SetApplicationVersion("1.0.0")` to enable `--version` flag (UX-01). Existing `AddExample` calls covering `{*}` and `->` already satisfied UX-01 wildcard/replace requirements.

**`tests/FileRevamp.Tests/Commands/RenameCommandTests.cs`** — 3 new tests + 1 extended:

- **`DryRun_WithCollision_ShowsAutoNumberedNamesInOutput`** (new): two files that both produce `prefixsuffix.csv` via wildcard `_v{*}_` → second gets `prefixsuffix(1).csv` in dry-run output (SAFE-02)
- **`LiveRun_FailingRename_CreatesLogFile`** (new): `report.csv --replace "csv->"` produces `report.` (trailing dot → FailedResult) → `rename-failures.log` created in target dir, exit code 1 (RPRT-03)
- **`LiveRun_LogFileInDirectory_IsExcludedFromBatch`** (new): pre-existing `rename-failures.log` in target dir is silently excluded (not skipped with message), normal file processed normally (RPRT-03, Pitfall 3)
- **`Help_ExitsZero_AndDescribesFlagsAndSyntax`** (extended): now also asserts output contains `{*}` and `->` (UX-01)

## Test Results

- 5 `FailureLoggerTests` added (RED→GREEN via TDD)
- 3 new `RenameCommandTests` added + 1 extended (RED→GREEN via TDD)
- **Full suite: 66 tests pass, 0 fail**

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Test case adjusted: `_new.csv` removal does not produce empty-stem failure**
- **Found during:** Task 2 RED phase, when `LiveRun_FailingRename_CreatesLogFile` exited 0 instead of 1
- **Issue:** `Path.GetFileNameWithoutExtension(".csv")` returns `".csv"` on Windows (not `""`)—so `.csv` passes `ValidateOutputName` (non-empty stem) and the rename succeeds
- **Fix:** Changed test to `report.csv --replace "csv->"` which produces `"report."` (trailing dot) — correctly rejected by `ValidateOutputName`
- **Files modified:** `tests/FileRevamp.Tests/Commands/RenameCommandTests.cs`
- **Commit:** 1454896

**2. [Rule 1 - Bug] Test assertion adjusted: collision produces `prefixsuffix.csv` not `prefix_suffix.csv`**
- **Found during:** Task 2 RED phase
- **Issue:** Wildcard pattern `_v{*}_` removes `_v1_` including both surrounding underscores → `prefix_v1_suffix.csv` → `prefixsuffix.csv` (not `prefix_suffix.csv`)
- **Fix:** Updated test assertion to check for `prefixsuffix.csv` and `prefixsuffix(1).csv`
- **Files modified:** `tests/FileRevamp.Tests/Commands/RenameCommandTests.cs`
- **Commit:** 1454896

**3. [Rule 3 - Blocker] Worktree missing plan 02-01 artifacts (RenameProposal, CollisionResolver, refactored RenameOrchestrator)**
- **Found during:** Task 2 GREEN phase compile error — `RenameOrchestrator` did not have `Plan()` method
- **Issue:** Worktree branch was created from `main`; plan 02-01 changes existed on `feature/phase-02-safety-and-reporting` only
- **Fix:** Cherry-picked commits `024cf06` (RenameProposal + CollisionResolver) and `509c378` (updated orchestrator tests) into worktree; applied `RenameOrchestrator.cs` directly via `git show 5ff21e7:...` to avoid conflict with already-written RenameCommand Phase 2 code
- **No additional files modified** (cherry-pick committed directly)

## All Four Phase 2 Success Criteria Verified

| Criterion | Test | Status |
|-----------|------|--------|
| SAFE-01/02: Two colliding files both renamed; second auto-numbered | `Plan_TwoFilesComputeSameName_BothProposals_SecondHasNumberedName` (orchestrator test) | PASS |
| SAFE-01/02: Dry-run with colliding batch shows auto-numbered names | `DryRun_WithCollision_ShowsAutoNumberedNamesInOutput` | PASS |
| RPRT-03: Runtime failure produces rename-failures.log with timestamped line | `LiveRun_FailingRename_CreatesLogFile` | PASS |
| UX-01: --help displays wildcard example and replace example | `Help_ExitsZero_AndDescribesFlagsAndSyntax` | PASS |

## Known Stubs

None — all functionality is fully implemented and wired.

## Threat Flags

No new trust boundaries introduced. `FailureLogger` writes to `directoryPath` (already resolved via `Path.GetFullPath` at command entry, T-01-02). Log filename is a hardcoded constant with no user-controlled path segments (T-04-01 mitigated as planned).

## Self-Check: PASSED

- [x] `FailureLogger.cs` created: sealed class, string directoryPath constructor, `LogFilePath` property, `Log()` method using `File.AppendAllText`
- [x] `FailureLogger.cs` does NOT contain `IFileSystem` parameter (only in documentation comment)
- [x] `FailureLogger.cs` uses `File.AppendAllText` (confirmed by grep)
- [x] No file creation in constructor (no `File.Create` or `new StreamWriter` in constructor)
- [x] All 5 FailureLoggerTests pass
- [x] `RenameCommand.cs` contains `const string LogFileName = "rename-failures.log"` and `OrdinalIgnoreCase` filter
- [x] `RenameCommand.cs` contains `new FailureLogger(directoryPath)` and `failureLogger.Log(` loop
- [x] `RenameCommand.cs` contains `orchestrator.Plan(filePaths,` and `orchestrator.Execute(proposals,`
- [x] `RenameCommand.cs` contains `earlyResults.Concat(executeResults)`
- [x] `Program.cs` contains `SetApplicationVersion`
- [x] `Program.cs` contains `AddExample` with `{*}` and `->`
- [x] All 8 RenameCommandTests pass (5 existing + 3 new)
- [x] Full suite passes: 66 tests, 0 failures
- [x] Help test asserts output contains `{*}` and `->`
