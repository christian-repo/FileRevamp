---
phase: 03-polish-and-packaging
plan: "01"
subsystem: core
tags: [code-quality, test-infrastructure, cross-platform, version, logging, validation]
dependency_graph:
  requires: []
  provides:
    - MockFileSystem in test assembly (tests/FileRevamp.Tests/Fakes)
    - Assembly-derived version string in Program.cs
    - UTC log timestamps with Z suffix in FailureLogger
    - Validation guard in RenameSettings preventing no-op invocations
  affects:
    - tests/FileRevamp.Tests/Core/CollisionResolverTests.cs
    - tests/FileRevamp.Tests/Core/RenameOrchestratorTests.cs
    - tests/FileRevamp.Tests/Core/FileDiscoveryTests.cs
    - tests/FileRevamp.Tests/Output/FailureLoggerTests.cs
tech_stack:
  added: []
  patterns:
    - MockFileSystem moved to FileRevamp.Tests.Fakes namespace (test-only)
    - Platform-neutral paths via Path.Combine(Path.GetTempPath(), ...) in test constants
    - UTC timestamps with Z suffix for log entries
key_files:
  created:
    - tests/FileRevamp.Tests/Fakes/MockFileSystem.cs
  modified:
    - src/FileRevamp/Program.cs
    - src/FileRevamp/Output/FailureLogger.cs
    - src/FileRevamp/Commands/RenameSettings.cs
    - tests/FileRevamp.Tests/Core/CollisionResolverTests.cs
    - tests/FileRevamp.Tests/Core/RenameOrchestratorTests.cs
    - tests/FileRevamp.Tests/Core/FileDiscoveryTests.cs
    - tests/FileRevamp.Tests/Output/FailureLoggerTests.cs
  deleted:
    - src/FileRevamp/Core/MockFileSystem.cs
decisions:
  - MockFileSystem moved to FileRevamp.Tests.Fakes; no test infrastructure in the production assembly
  - ExportsDir changed to Path.Combine(Path.GetTempPath(), ...) in CollisionResolverTests and RenameOrchestratorTests; inline MockFileSystem paths and FileExists assertions updated to stay consistent
  - FailureLoggerTests regex updated to require Z suffix alongside the UTC fix
metrics:
  duration: "~25 minutes"
  completed_date: "2026-06-03"
  tasks_completed: 3
  tasks_total: 3
  files_changed: 8
---

# Phase 03 Plan 01: Code Quality Fixes Summary

**One-liner:** MockFileSystem moved to test assembly, cross-platform test paths normalized, assembly-derived version, UTC log timestamps, and no-op validation guard added.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Move MockFileSystem to test assembly | 3723687 | Fakes/MockFileSystem.cs created, Core/MockFileSystem.cs deleted, 3 test consumers updated |
| 2 | Fix cross-platform test paths | 750a1f7 | CollisionResolverTests.cs, RenameOrchestratorTests.cs |
| 3 | Program.cs version, FailureLogger UTC, validation guard | 2a83536 | Program.cs, FailureLogger.cs, RenameSettings.cs, FailureLoggerTests.cs |

## Objective Outcome

All four code-quality issues from the Phase 2 review resolved:

- **WR-04** (MockFileSystem in production assembly): Moved to `tests/FileRevamp.Tests/Fakes/MockFileSystem.cs` with namespace `FileRevamp.Tests.Fakes`. Updated consumers: `CollisionResolverTests`, `RenameOrchestratorTests`, `FileDiscoveryTests`.
- **IN-02/IN-03** (Unix-only test paths): `ExportsDir` constant in `CollisionResolverTests` and `RenameOrchestratorTests` changed from `"/exports"` to `Path.Combine(Path.GetTempPath(), "filerevamp_test_exports")`. MockFileSystem initializer file paths and `FileExists` assertions updated to use consistent ExportsDir-derived paths.
- **IN-01** (hardcoded version string): `Program.cs` now derives version from `typeof(Program).Assembly.GetName().Version?.ToString() ?? "dev"`.
- **WR-01** (local timestamps): `FailureLogger` now uses `DateTime.UtcNow` with a `Z` suffix.
- **WR-03** (no-op invocation): `RenameSettings.Validate()` now returns `ValidationResult.Error(...)` when neither `--remove` nor `--replace` is provided.

## Verification

```
dotnet build  → Build succeeded, 0 warnings, 0 errors
dotnet test   → Passed! 73/73 tests green on Windows
grep -r "MockFileSystem" src/ → only XML doc comments (no code references)
grep "DateTime.UtcNow" src/FileRevamp/Output/FailureLogger.cs → match found
grep "Assembly.GetName" src/FileRevamp/Program.cs → match found
```

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] FileDiscoveryTests also referenced MockFileSystem in production namespace**
- **Found during:** Task 1 build verification
- **Issue:** `dotnet build` failed — `FileDiscoveryTests.cs` used `MockFileSystem` from `FileRevamp.Core` alongside `CollisionResolverTests` and `RenameOrchestratorTests`. The plan listed only two consumers but there were three.
- **Fix:** Added `using FileRevamp.Tests.Fakes;` to `FileDiscoveryTests.cs`.
- **Files modified:** `tests/FileRevamp.Tests/Core/FileDiscoveryTests.cs`
- **Commit:** 3723687

**2. [Rule 1 - Bug] CollisionResolverTests disk-exists tests would break if ExportsDir changed without updating MockFileSystem keys**
- **Found during:** Task 2 analysis
- **Issue:** The plan said "only change the directoryPath constant, not the MockFileSystem constructor paths." But `CollisionResolver.Resolve()` calls `MockFileSystem.Combine(directoryPath, desiredName)` which produces a path with the new tempPath prefix. `FileExists` then looks up that key in the mock — which wouldn't match the original `/exports/report.csv` keys. Tests like `Resolve_DesiredNameOnDisk_ReturnsNumbered` and `Resolve_SlotOneOccupied_ReturnsTwoSuffix` would fail.
- **Fix:** Updated MockFileSystem constructor file paths and FileExists assertions in both `CollisionResolverTests` and `RenameOrchestratorTests` to use `Path.Combine(ExportsDir, filename)` or the `F()` helper.
- **Files modified:** `tests/FileRevamp.Tests/Core/CollisionResolverTests.cs`, `tests/FileRevamp.Tests/Core/RenameOrchestratorTests.cs`
- **Commit:** 750a1f7

**3. [Rule 1 - Bug] FailureLoggerTests regex did not account for Z suffix**
- **Found during:** Task 3 test run
- **Issue:** `FailureLogger_Log_LineFormat_ContainsTimestamp_And_FAILPrefix` used regex `\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\]` which doesn't match the new `...ssZ]` format.
- **Fix:** Updated regex to `\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}Z\]`.
- **Files modified:** `tests/FileRevamp.Tests/Output/FailureLoggerTests.cs`
- **Commit:** 2a83536

## Known Stubs

None.

## Threat Flags

None — no new network endpoints, auth paths, file access patterns, or schema changes introduced.

## Self-Check: PASSED

- `tests/FileRevamp.Tests/Fakes/MockFileSystem.cs` — FOUND
- `src/FileRevamp/Core/MockFileSystem.cs` — deleted (FOUND absent, as expected)
- `src/FileRevamp/Program.cs` contains `Assembly.GetName().Version` — FOUND
- `src/FileRevamp/Output/FailureLogger.cs` contains `DateTime.UtcNow` — FOUND
- `src/FileRevamp/Commands/RenameSettings.cs` has Validate() no-op guard — FOUND
- Commits 3723687, 750a1f7, 2a83536 — all exist in git log
- All 73 tests green on Windows
