---
phase: 03-polish-and-packaging
fixed_at: 2026-06-03T00:00:00Z
review_path: .planning/phases/03-polish-and-packaging/03-REVIEW.md
iteration: 1
findings_in_scope: 6
fixed: 6
skipped: 0
status: all_fixed
---

# Phase 03: Code Review Fix Report

**Fixed at:** 2026-06-03T00:00:00Z
**Source review:** `.planning/phases/03-polish-and-packaging/03-REVIEW.md`
**Iteration:** 1

**Summary:**
- Findings in scope: 6 (1 Critical + 5 Warning; IN-01 excluded by fix_scope=critical_warning)
- Fixed: 6
- Skipped: 0

## Fixed Issues

### CR-01: CollisionResolver false-positive for source-file destinations

**Files modified:** `src/FileRevamp/Core/CollisionResolver.cs`, `src/FileRevamp/Core/RenameOrchestrator.cs`, `tests/FileRevamp.Tests/Core/CollisionResolverTests.cs`
**Commit:** a231cd3
**Applied fix:**
- `CollisionResolver` constructor now accepts a `HashSet<string> sourceNames` parameter representing all source filenames in the batch.
- `Resolve` checks disk existence only when the name is NOT in `sourceNames` — a name occupied solely by a source file is treated as free (it will vacate during Execute).
- `RenameOrchestrator.Plan()` builds `sourceNames` from all input file paths before the planning loop, renames `claimed` to `claimedDestinations` for clarity, and passes `sourceNames` to the resolver.
- Updated `CollisionResolverTests` to use the new constructor signature (with `NoSources()` helper for isolation tests) and added two new regression tests: one verifying the false-positive is eliminated, one verifying a genuine non-source disk conflict still produces auto-numbering.
- All 81 tests pass.

### WR-01: FailureLogger.Log unhandled exception

**Files modified:** `src/FileRevamp/Commands/RenameCommand.cs`
**Commit:** 92f0682
**Applied fix:** Wrapped the `failureLogger.Log(...)` call in a `try-catch` block catching `IOException or UnauthorizedAccessException`. On catch, emits a `[yellow]Warning: could not write to failure log: ...[/]` console message via `_console.MarkupLine` instead of crashing. Chosen approach: inline catch in `RenameCommand` (simpler, no new exception type needed).

### WR-02: --replace case-sensitivity undocumented in help text

**Files modified:** `src/FileRevamp/Commands/RenameSettings.cs`
**Commit:** a6eb484
**Applied fix:** Updated the `[Description]` attribute on `ReplaceOperations` to include the sentence "The find string is case-sensitive." so users see the behavioral difference in `--help` output without having to discover it by trial and error.

### WR-03: WildcardPatternMatcher empty-stem guard missing for unanchored path

**Files modified:** `src/FileRevamp/Core/WildcardPatternMatcher.cs`
**Commit:** 8d36fb6
**Applied fix:** Added the same `afterRemove.Length == 0 && extension.Length > 0 → return null` guard to the `else if (removeRegex.IsMatch(currentStem))` branch that already existed in the anchored-match branch. Both code paths now consistently return `null` (SkippedResult) when the removal would produce an extension-only filename, rather than one returning `null` (Skip) and the other returning `.ext` (FailedResult via ValidateOutputName).

### WR-04: IFileSystem.GetFiles searchPattern parameter is dead

**Files modified:** `src/FileRevamp/Core/IFileSystem.cs`, `src/FileRevamp/Core/FileSystem.cs`, `src/FileRevamp/Core/DryRunFileSystem.cs`, `src/FileRevamp/Core/FileDiscovery.cs`, `tests/FileRevamp.Tests/Fakes/MockFileSystem.cs`
**Commit:** 6192d78
**Applied fix:** Removed `searchPattern` parameter from `IFileSystem.GetFiles` interface and all four implementations. `FileSystem.GetFiles` now hard-codes `"*"` in the `Directory.GetFiles` call (the only value that was ever passed). `DryRunFileSystem` and `MockFileSystem` updated accordingly. `FileDiscovery` call sites updated to remove the `"*"` argument.

### WR-05: MockFileSystem silently ignores searchPattern

**Files modified:** `tests/FileRevamp.Tests/Fakes/MockFileSystem.cs`
**Commit:** 6192d78 (same commit as WR-04 — applying WR-04 resolved WR-05 automatically)
**Applied fix:** By removing the `searchPattern` parameter from the interface and `MockFileSystem` implementation (WR-04), the silent-ignore problem is eliminated entirely. No residual dead parameter remains.

---

_Fixed: 2026-06-03T00:00:00Z_
_Fixer: Claude (gsd-code-fixer)_
_Iteration: 1_
