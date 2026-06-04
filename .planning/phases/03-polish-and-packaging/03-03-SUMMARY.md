---
phase: 03-polish-and-packaging
plan: "03"
subsystem: tests
tags: [integration-tests, edge-cases, unicode, collision, log-exclusion, wildcard]
dependency_graph:
  requires:
    - 03-01 (build clean, 73 tests passing)
    - 03-02 (IFileSystem injection seam in RenameCommand)
  provides:
    - Six integration tests covering all D-02 edge cases
    - Regression guard for literal dots/parens wildcard handling
    - Regression guard for batch collision auto-numbering live run
    - Regression guard for log-file exclusion (RPRT-03)
    - Regression guard for empty-directory clean exit
    - Regression guard for unicode filename rename success
    - Regression guard for long filename (244-char) rename success
  affects:
    - tests/FileRevamp.Tests/Commands/EdgeCaseIntegrationTests.cs
tech_stack:
  added: []
  patterns:
    - CommandAppTester + FakeTypeRegistrar pattern (copied from RenameCommandTests.cs)
    - Real disk I/O via temp directories for all edge-case tests
    - FluentAssertions with "because:" parameter on non-obvious assertions
    - IDisposable cleanup of temp directories after each test class run
key_files:
  created:
    - tests/FileRevamp.Tests/Commands/EdgeCaseIntegrationTests.cs
  modified: []
decisions:
  - All 6 edge-case tests use real disk I/O (no MockFileSystem injection) — these scenarios require real filesystem behavior with real filenames
  - Tasks 1 and 2 committed together since they constitute a single cohesive file creation
  - TDD protocol applied: tests written first; all 6 passed on first run against existing production code (production code already handled all edge cases correctly)
metrics:
  duration: "~3 minutes"
  completed_date: "2026-06-03"
  tasks_completed: 2
  tasks_total: 2
  files_changed: 1
---

# Phase 03 Plan 03: Edge-Case Integration Tests Summary

**One-liner:** Six CommandAppTester integration tests added covering literal dots/parens, batch collision live run, log-file exclusion, empty directory, unicode filenames, and long filenames — all D-02 scenarios verified passing.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Write EdgeCaseIntegrationTests — literal dots/parens, batch collision live run, log-file exclusion | 4bea766 | tests/FileRevamp.Tests/Commands/EdgeCaseIntegrationTests.cs (created, all 6 tests) |
| 2 | Add EdgeCaseIntegrationTests — empty directory, unicode filenames, long filenames | 4bea766 | (same commit — appended to file during Task 1 creation) |

## Objective Outcome

All six D-02 edge-case scenarios covered by integration tests running through the full CLI via CommandAppTester:

- **Test 1 — LiteralDotsAndParensInPattern_RenamesCorrectly**: Verifies `report.new.(2024).csv` renames to `report.new.csv` with `--remove ".(2024)"`. Confirms WildcardCompiler's Regex.Escape-first step (Step 1) prevents `.`, `(`, `)` from being treated as regex metacharacters.
- **Test 2 — BatchCollision_LiveRun_BothFilesRenameWithAutoNumbering**: Verifies `prefix_report.csv` and `suffix_report.csv` both renamed via `--replace "prefix_->"` and `--replace "suffix_->"` — producing `report.csv` and `report(1).csv` on disk. No files skipped.
- **Test 3 — LogFileExcluded_EvenWhenPatternWouldMatchIt**: Verifies `rename-failures.log` is excluded from the batch even when `--remove rename-` would match its name. The adjacent `rename-failures_new.txt` IS processed normally.
- **Test 4 — EmptyDirectory_ExitsZero_WithZeroRenamedCount**: Verifies clean exit (code 0) and "Renamed: 0" output for an empty directory.
- **Test 5 — UnicodeFilename_RenamedSuccessfully**: Verifies `café_new.csv` renames to `café.csv` (success, not graceful failure — per D-03 decision binding).
- **Test 6 — LongFilename_ProcessedWithoutError**: Verifies a 244-character filename (`aaa...a_new.csv`) renamed without error — well within Windows MAX_PATH 255 for standalone filenames.

## Verification

```
dotnet test FileRevamp.sln --filter "FullyQualifiedName~EdgeCaseIntegrationTests"
→ Passed! 6/6 edge-case tests green

dotnet test FileRevamp.sln
→ Passed! 79/79 tests green (73 pre-existing + 6 new)

grep -c "[Fact]" tests/FileRevamp.Tests/Commands/EdgeCaseIntegrationTests.cs
→ 6
```

## Deviations from Plan

### Auto-fixed Issues

None — plan executed exactly as written. All 6 tests passed on first run without any fixes to production code. The production code (WildcardCompiler, CollisionResolver, RenameCommand) already handled all edge cases correctly.

### TDD Gate Compliance

Both tasks have `tdd="true"` in the plan. The tests were written and committed before any production code modification was attempted. All 6 tests passed immediately because the production code already handled the edge cases. No GREEN-phase production changes were needed — this is a valid TDD outcome when tests are added retroactively to verify existing correct behavior.

- RED gate: test file written; tests ran against existing production code
- GREEN gate: all 6 passed without implementation changes
- No REFACTOR needed

## Known Stubs

None.

## Threat Flags

None — no new network endpoints, auth paths, file access patterns, or schema changes introduced. Test files use temp directories on disk with real filenames; temp directories are cleaned up in Dispose().

## Self-Check: PASSED

- `tests/FileRevamp.Tests/Commands/EdgeCaseIntegrationTests.cs` — FOUND
- Contains 6 `[Fact]` attributes — CONFIRMED
- Commit `4bea766` exists in git log — CONFIRMED
- All 79 tests green (73 pre-existing + 6 new) — CONFIRMED
- No temp directory leaks: Dispose() iterates `_tempDirs` and deletes each — CONFIRMED
