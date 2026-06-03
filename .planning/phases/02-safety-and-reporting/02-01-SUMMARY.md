---
plan: "02-01"
phase: "02-safety-and-reporting"
status: complete
started: "2026-06-02"
completed: "2026-06-02"
requirements: [SAFE-01, SAFE-02]
key-files:
  created:
    - src/FileRevamp/Core/RenameProposal.cs
    - src/FileRevamp/Core/CollisionResolver.cs
    - tests/FileRevamp.Tests/Core/CollisionResolverTests.cs
  modified:
    - src/FileRevamp/Core/RenameOrchestrator.cs
    - src/FileRevamp/Commands/RenameCommand.cs
    - tests/FileRevamp.Tests/Core/RenameOrchestratorTests.cs
---

## What Was Built

Implemented the two-pass rename orchestrator (SAFE-01, SAFE-02):

1. **`RenameProposal` record** — Immutable planning record with `SourcePath`, `OriginalName`, `ResolvedName`, and `WouldChange`. One instance per file that will be renamed.

2. **`CollisionResolver` class** — Windows-style auto-numbering: `report.csv` → `report(1).csv` → `report(2).csv`. Checks both the in-batch `claimed` HashSet (OrdinalIgnoreCase) and `IFileSystem.FileExists` before assigning any slot. Mutates `claimed` in place so sequential calls for the same desired name each get the next available slot.

3. **`RenameOrchestrator` refactored** — Replaced the single-pass lazy `IEnumerable<RenameResult> Execute()` with:
   - `Plan(filePaths, patternMatcher, replaceTransforms, directoryPath)` → `(IReadOnlyList<RenameProposal>, IReadOnlyList<RenameResult>)` — computes all proposals eagerly, resolves collisions, returns skipped/failed files as early results. No `MoveFile` calls.
   - `Execute(proposals, directoryPath, dryRun)` → `IReadOnlyList<RenameResult>` — acts on the plan. Dry-run returns tagged results; live run calls `MoveFile` per proposal.

4. **`RenameCommand` stub** — `FileDiscovery` moved from orchestrator to command; command now calls `Plan()` + `Execute()` in sequence. Plan 02 will add the log-file exclusion filter and `FailureLogger` wiring on top.

## Test Results

- 7 `CollisionResolverTests` added (RED→GREEN via TDD)
- 9 `RenameOrchestratorTests` (6 existing updated to Plan+Execute API, 3 new collision tests)
- **Full suite: 65 tests pass, 0 fail**

Key collision tests verified:
- `Plan_TwoFilesComputeSameName_BothProposals_SecondHasNumberedName`: two files computing to `report.csv` → proposals[0].ResolvedName=`report.csv`, proposals[1].ResolvedName=`report(1).csv`
- `Execute_NoMoveCalledBeforePlanCompletes`: `MoveCallCount=0` after Plan(), `MoveCallCount=2` after Execute()

## Deviations

None. Plan followed exactly.

## Self-Check: PASSED

- [x] `RenameProposal.cs` created with `record RenameProposal(string SourcePath, string OriginalName, string ResolvedName, bool WouldChange)`
- [x] `CollisionResolver.cs` created with `sealed class CollisionResolver`, constructor-injected `IFileSystem`, `string Resolve(string directoryPath, string desiredName, HashSet<string> claimed)`
- [x] `RenameOrchestrator.cs` contains `Plan()` returning tuple and `Execute(proposals, dir, dryRun)` — old single-pass Execute removed
- [x] `CollisionResolver.cs` uses `_fileSystem.Combine()` for all path construction (not `System.IO.Path.Combine` directly)
- [x] `RenameOrchestrator.cs` contains `new CollisionResolver(_fileSystem)` in Plan() body
- [x] `RenameOrchestrator.cs` no longer contains Phase 1 stub comment "conflict resolution coming in Phase 2"
- [x] All 7 CollisionResolverTests pass
- [x] All 9 RenameOrchestratorTests pass (including 3 new collision tests)
- [x] Full suite passes (65 tests, 0 failures)
