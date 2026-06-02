---
phase: 01-core-rename-pipeline
plan: "01"
subsystem: core
tags: [scaffold, wildcard, rename-engine, ifilesystem, walking-skeleton]
dependency_graph:
  requires: []
  provides:
    - FileRevamp.sln solution with net9.0 projects
    - IFileSystem seam (FileSystem/DryRunFileSystem/MockFileSystem)
    - WildcardCompiler.ToRegex with strict escape-substitute-anchor order
    - WildcardPatternMatcher.ApplyRemoves (stem-only with extension preservation)
    - RenameOrchestrator pipeline (scan->transform->conflict->dry/live)
    - RenameResult value object
    - RenameCommand + RenameSettings CLI entry point
  affects:
    - All Phase 2 plans (extend orchestrator and file discovery)
    - All Phase 3 plans (packaging, polish)
tech_stack:
  added:
    - Spectre.Console.Cli 0.55.0 (CLI parsing + colored output)
    - Microsoft.Extensions.FileSystemGlobbing 9.* (file discovery, Phase 2)
    - xUnit 2.9.2 (test runner)
    - FluentAssertions 7.* (assertion library)
    - Spectre.Console.Cli.Testing 0.55.0 (CLI test harness, Phase 2 usage)
  patterns:
    - Command<TSettings> + CommandSettings pattern (Spectre.Console.Cli)
    - IFileSystem seam for dry-run and test isolation
    - TDD RED/GREEN cycle with table-driven tests
    - Escape-then-substitute wildcard-to-regex conversion order
key_files:
  created:
    - FileRevamp.sln
    - src/FileRevamp/FileRevamp.csproj
    - src/FileRevamp/Program.cs
    - src/FileRevamp/Commands/RenameCommand.cs
    - src/FileRevamp/Commands/RenameSettings.cs
    - src/FileRevamp/Core/IFileSystem.cs
    - src/FileRevamp/Core/FileSystem.cs
    - src/FileRevamp/Core/DryRunFileSystem.cs
    - src/FileRevamp/Core/MockFileSystem.cs
    - src/FileRevamp/Core/WildcardCompiler.cs
    - src/FileRevamp/Core/WildcardPatternMatcher.cs
    - src/FileRevamp/Core/RenameOrchestrator.cs
    - src/FileRevamp/Output/RenameResult.cs
    - tests/FileRevamp.Tests/FileRevamp.Tests.csproj
    - tests/FileRevamp.Tests/Core/WildcardCompilerTests.cs
    - tests/FileRevamp.Tests/Core/RenameOrchestratorTests.cs
  modified: []
decisions:
  - "WildcardPatternMatcher.ApplyRemoves operates on filename stem only (extension preserved separately) so that {*} at end of pattern does not consume the dot-extension separator"
  - "ApplyRemoves replacement string is _$1 style (literal-prefix + $1 capture group) rather than empty-string replace, enabling the pattern to keep the prefix while removing the literal middle and trailing wildcard"
  - "MockFileSystem normalizes backslash/forward-slash separators to forward-slash for cross-platform test compatibility"
metrics:
  duration: "17 minutes"
  completed_date: "2026-06-01"
  tasks_completed: 2
  files_created: 16
---

# Phase 01 Plan 01: Walking Skeleton — Solution Scaffold + Core Pipeline Summary

Delivered the complete FileRevamp Walking Skeleton: net9.0 console app with Spectre.Console.Cli 0.55.0, IFileSystem seam (FileSystem/DryRunFileSystem/MockFileSystem), WildcardCompiler (escape-substitute-anchor order), WildcardPatternMatcher (stem-only application), RenameOrchestrator (scan-transform-conflict-dry/live pipeline), and CLI entry point — all verified by 21 passing tests.

## Verification Results

- `dotnet build FileRevamp.sln` — PASS (0 errors, 0 warnings)
- `dotnet test tests/FileRevamp.Tests` — PASS (21/21 tests)
- `dotnet run --project src/FileRevamp -- --help` — exit 0, shows `--remove`, `--dry-run`, `{*}`
- `dotnet run --project src/FileRevamp -- /tmp/nodir --remove "_{*}new_{*}" --dry-run` — exit 0, graceful "directory not found" message

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 (RED) | WildcardCompiler failing tests | 6da121d | FileRevamp.sln, csproj files, WildcardCompilerTests.cs |
| 1 (GREEN) | IFileSystem seam + WildcardCompiler impl | 3855130 | IFileSystem.cs, FileSystem.cs, DryRunFileSystem.cs, MockFileSystem.cs, WildcardCompiler.cs, RenameResult.cs |
| 2 (RED) | RenameOrchestrator failing tests | caa8ba2 | RenameOrchestratorTests.cs |
| 2 (GREEN) | CLI + orchestrator impl | c1538d4 | RenameCommand.cs, RenameSettings.cs, RenameOrchestrator.cs, WildcardPatternMatcher.cs, Program.cs |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Regex.Escape closing brace behavior differs from PITFALLS.md documentation**
- **Found during:** Task 1, WildcardCompiler implementation
- **Issue:** PITFALLS.md Pitfall 4 states the escaped form of `{*}` is `\{\*\}` but `Regex.Escape("{*}")` actually produces `\{\*}` (closing `}` is NOT escaped by Regex.Escape)
- **Fix:** Changed substitution from `.Replace(@"\{\*\}", "(.*)")` to `.Replace(@"\{\*}", "(.*)")` (and similarly for `{+}` and `{?}`)
- **Files modified:** `src/FileRevamp/Core/WildcardCompiler.cs`
- **Commit:** 3855130

**2. [Rule 1 - Bug] WildcardPatternMatcher ApplyRemoves must operate on stem, not full filename**
- **Found during:** Task 2, RenameOrchestrator E2E test failures
- **Issue:** The plan spec says `regex.Replace(filename, "")` but with an anchored regex `^_(.*)(new)_(.*)$`, replacing the entire match with `""` gives `""` not the expected `_foo_.csv`. The `{*}` at the end greedily consumes the extension making the literal-replace approach impossible.
- **Fix:** Split filename into stem + extension before applying patterns. Replace the full-stem match using a computed replacement string `_$1` (literal-prefix + first capture group). The `BuildRemoveReplacement()` helper in WildcardCompiler constructs this string from the original wildcard pattern.
- **Files modified:** `src/FileRevamp/Core/WildcardPatternMatcher.cs`, `src/FileRevamp/Core/WildcardCompiler.cs`
- **Commit:** c1538d4

**3. [Rule 1 - Bug] MockFileSystem path separator normalization required for Windows**
- **Found during:** Task 2, LiveRun test failure
- **Issue:** `Path.GetDirectoryName("/exports/_foo_new_bar.csv")` returns `\exports` (backslash) on Windows, but test used `/exports` (forward slash). `Path.Combine("/exports", "_foo_.csv")` produces `/exports\_foo_.csv` (mixed), causing key mismatches in the dictionary.
- **Fix:** Added `Normalize(path) => path.Replace('\\', '/')` to MockFileSystem; all paths normalized to forward-slash before dictionary lookups and key storage.
- **Files modified:** `src/FileRevamp/Core/MockFileSystem.cs`
- **Commit:** c1538d4

**4. [Rule 1 - Bug] Spectre.Console.Cli 0.55.0 API differences from STACK.md expectations**
- **Found during:** Task 2, build errors
- **Issue 1:** `Command<T>.Execute` is `protected` in 0.55.0 (not `public` as implied by plan spec).
- **Issue 2:** `ValidationResult` lives in `Spectre.Console` namespace, not `Spectre.Console.Cli`.
- **Issue 3:** `Description` attribute containing `[DRY RUN]` was interpreted as Spectre markup, causing exit 127 on `--help`.
- **Fix:** Changed override to `protected`, added `using Spectre.Console;`, changed description to "Displays a DRY RUN prefix..." (no brackets).
- **Files modified:** `src/FileRevamp/Commands/RenameCommand.cs`, `src/FileRevamp/Commands/RenameSettings.cs`
- **Commit:** c1538d4

### Security Mitigations Applied (Threat Model)

- **T-01-01 (Path traversal):** RenameOrchestrator verifies `Path.GetDirectoryName(destPath) == normalizedDir` after computing destination path. Any path that escapes the source directory is rejected as `FailedResult("Path traversal rejected")`.
- **T-01-02 (Path resolution):** RenameCommand resolves `settings.Path` to absolute path via `Path.GetFullPath` at entry, before passing to orchestrator.
- **T-01-05 (System files):** `GetFiles` scans only the user-supplied directory (non-recursive, `SearchOption.TopDirectoryOnly`). No mechanism to target files outside the scan directory.

## Known Stubs

None. The Walking Skeleton is functional end-to-end; no placeholder data flows to UI rendering.

## Threat Flags

No new security surface beyond what was analyzed in the plan's threat model.

## Self-Check: PASSED

- [x] `FileRevamp.sln` exists
- [x] `src/FileRevamp/FileRevamp.csproj` exists
- [x] `src/FileRevamp/Core/IFileSystem.cs` exists
- [x] `src/FileRevamp/Core/WildcardCompiler.cs` exists
- [x] `src/FileRevamp/Core/RenameOrchestrator.cs` exists
- [x] `src/FileRevamp/Output/RenameResult.cs` exists
- [x] `tests/FileRevamp.Tests/Core/WildcardCompilerTests.cs` exists
- [x] `tests/FileRevamp.Tests/Core/RenameOrchestratorTests.cs` exists
- [x] Commit 6da121d exists (RED Task 1)
- [x] Commit 3855130 exists (GREEN Task 1)
- [x] Commit caa8ba2 exists (RED Task 2)
- [x] Commit c1538d4 exists (GREEN Task 2)
