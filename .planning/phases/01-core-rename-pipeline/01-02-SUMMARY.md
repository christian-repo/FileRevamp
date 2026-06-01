---
phase: 01-core-rename-pipeline
plan: "02"
subsystem: core
tags: [file-discovery, glob, replace-transform, wildcard-matcher, nfc-normalization, rename-pipeline]

dependency_graph:
  requires:
    - phase: 01-core-rename-pipeline/01-01
      provides: IFileSystem seam, WildcardCompiler, WildcardPatternMatcher, RenameOrchestrator, MockFileSystem, walking skeleton
  provides:
    - FileDiscovery class with glob-based file enumeration (TARG-02)
    - ReplaceTransform class with literal find/replace and Parse factory (PAT-02)
    - WildcardCompiler.ToRegex anchored=false parameter for unanchored (substring) regex
    - WildcardPatternMatcher dual-regex: anchored gate + unanchored remove, NFC normalization
    - RenameOrchestrator with 5-param Execute: globPattern + replaceTransforms, fixed PAT-03 order
    - RenameCommand updated to parse --replace operands and glob from path argument
    - Full end-to-end pipeline: TARG-01/02, PAT-01/02/03, EXEC-01/02 all implemented
  affects:
    - 01-03 (output/reporter plan — will consume RenameResult pipeline)
    - All Phase 2 plans (packaging, polish)

tech-stack:
  added: []
  patterns:
    - Dual-regex compilation per pattern (anchored for IsMatch gate, unanchored for Regex.Replace)
    - Literal patterns use unanchored substring removal; wildcard patterns use BuildRemoveReplacement capture
    - NFC normalization on filename before pattern matching (Pitfall 10 mitigation)
    - Fixed operation order baked into RenameOrchestrator (removes → replaces, not configurable)
    - FileDiscovery wraps IFileSystem + FileSystemGlobbing Matcher for glob-filtered enumeration

key-files:
  created:
    - src/FileRevamp/Core/FileDiscovery.cs
    - src/FileRevamp/Core/ReplaceTransform.cs
    - tests/FileRevamp.Tests/Core/FileDiscoveryTests.cs
    - tests/FileRevamp.Tests/Core/ReplaceTransformTests.cs
    - tests/FileRevamp.Tests/Core/WildcardPatternMatcherTests.cs
  modified:
    - src/FileRevamp/Core/WildcardCompiler.cs (added anchored parameter)
    - src/FileRevamp/Core/WildcardPatternMatcher.cs (dual-regex, NFC normalization, HasPatterns)
    - src/FileRevamp/Core/RenameOrchestrator.cs (new 5-param Execute with glob + replaces)
    - src/FileRevamp/Commands/RenameCommand.cs (parse --replace, split glob from path)
    - tests/FileRevamp.Tests/Core/RenameOrchestratorTests.cs (updated to new signature + Tests A/B/C)

key-decisions:
  - "Dual-regex strategy for WildcardPatternMatcher: anchored regex for IsMatch gate (existing wildcard behavior preserved), unanchored regex for substring Remove (new literal pattern behavior). Both compiled at construction time."
  - "Literal patterns (no {*}/{+}/{?}) fall through the anchored-match check and use unanchored Regex.Replace with empty replacement, enabling substring removal without breaking existing capture-group logic."
  - "FileDiscovery.GetFiles uses FileSystemGlobbing.Matcher.Match(filenames) overload (IEnumerable<string>) for flat-directory glob filtering — no real filesystem traversal, compatible with MockFileSystem."
  - "RenameOrchestrator.Execute signature extended to 5 params (breaking change); all callers updated. No backward-compat overload added — clean break aligns with plan scope."
  - "T-02-01 mitigation: after all transforms, check Path.GetInvalidFileNameChars() and emit FailedResult if any invalid char present."

patterns-established:
  - "PAT-03 canonical order: patternMatcher.ApplyRemoves first, then foreach replaceTransform.Apply — never reversed."
  - "Replace-only mode: zero remove patterns → HasPatterns=false → all files selected, replaces apply to all."
  - "WildcardCompiler.ToRegex(pattern, anchored: bool) is now the canonical compilation entry point."

requirements-completed: [TARG-01, TARG-02, PAT-01, PAT-02, PAT-03, EXEC-01, EXEC-02]

duration: 28min
completed: 2026-05-31
---

# Phase 01 Plan 02: Full Rename Pipeline Summary

**Glob-filtered file discovery, literal replace transforms with PAT-03 operation order, and NFC-normalized dual-regex remove patterns extending the walking skeleton to a complete end-to-end rename pipeline.**

## Performance

- **Duration:** ~28 min
- **Started:** 2026-05-31T00:00:00Z
- **Completed:** 2026-05-31T00:28:00Z
- **Tasks:** 2 (each with RED + GREEN commits)
- **Files modified:** 9

## Accomplishments

- FileDiscovery with `FileSystemGlobbing.Matcher` for glob-based file selection (`*.csv`, `report_*`, etc.)
- ReplaceTransform with literal `string.Replace` (not regex) and `Parse("old->new")` factory
- WildcardCompiler `anchored=false` parameter for unanchored remove operations
- WildcardPatternMatcher dual-regex: anchored gate + unanchored substring removal + NFC normalization
- RenameOrchestrator updated to fixed PAT-03 order: removes → replaces, with invalid-char validation (T-02-01)
- RenameCommand parses `--replace` operands and splits glob from directory path argument
- 37/37 tests passing (16 new, 21 original all preserved)

## Task Commits

1. **Task 1 RED: Failing tests for FileDiscovery, ReplaceTransform, WildcardPatternMatcher NFC** - `32a1109` (test)
2. **Task 1 GREEN: FileDiscovery, ReplaceTransform, WildcardCompiler+WildcardPatternMatcher impl** - `44593ea` (feat)
3. **Task 2 RED: Failing tests for updated RenameOrchestrator (Tests A/B/C + signature)** - `9bcfc8b` (test)
4. **Task 2 GREEN: RenameOrchestrator, RenameCommand, WildcardPatternMatcher.HasPatterns** - `0b38f57` (feat)

## Files Created/Modified

- `src/FileRevamp/Core/FileDiscovery.cs` - Glob file enumeration via FileSystemGlobbing Matcher
- `src/FileRevamp/Core/ReplaceTransform.cs` - Literal find/replace with Parse("old->new") factory
- `src/FileRevamp/Core/WildcardCompiler.cs` - Added `anchored=true/false` parameter to ToRegex
- `src/FileRevamp/Core/WildcardPatternMatcher.cs` - Dual-regex, NFC normalization, HasPatterns property
- `src/FileRevamp/Core/RenameOrchestrator.cs` - 5-param Execute with FileDiscovery + ReplaceTransforms + T-02-01 guard
- `src/FileRevamp/Commands/RenameCommand.cs` - Parse --replace, split glob from path
- `tests/FileRevamp.Tests/Core/FileDiscoveryTests.cs` - 4 tests for glob filter scenarios
- `tests/FileRevamp.Tests/Core/ReplaceTransformTests.cs` - 6 tests for literal replace + Parse
- `tests/FileRevamp.Tests/Core/WildcardPatternMatcherTests.cs` - 3 tests for NFC + multi-literal remove
- `tests/FileRevamp.Tests/Core/RenameOrchestratorTests.cs` - Updated to new signature + Tests A/B/C

## Decisions Made

- **Dual-regex strategy:** Anchored regex for IsMatch gate (preserves existing wildcard capture behavior), unanchored regex for substring Remove (enables literal patterns like `_new` to remove substrings). Both compiled at construction time.
- **Literal fallthrough:** Patterns with no `{*}/{+}/{?}` tokens naturally pass through anchored-match (because `^_new$` won't match `file_new_name`) and use the unanchored Remove path.
- **FileDiscovery Matcher API:** Used `matcher.Match(IEnumerable<string>)` overload (filenames only) rather than the directory-based overload, because the directory-based overload requires actual filesystem access. This makes FileDiscovery work with MockFileSystem.
- **Breaking orchestrator signature:** Clean 5-param Execute, no backward-compat overload. All callers updated simultaneously.
- **T-02-01 applied inline:** Invalid filename chars checked immediately after all transforms, before path construction.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing Critical] FileSystemGlobbing Matcher API differs from plan spec**
- **Found during:** Task 1 (FileDiscovery implementation)
- **Issue:** Plan spec used `f.Stem + f.Extension` on `FilePatternMatch` but the struct only has `Path` and `Stem` properties — no `Extension`. Build error CS1061.
- **Fix:** Changed to `f.Path` (which contains the full relative path — just the filename in flat-directory mode)
- **Files modified:** `src/FileRevamp/Core/FileDiscovery.cs`
- **Verification:** Build succeeded, FileDiscoveryTests all pass
- **Committed in:** `44593ea` (Task 1 GREEN)

**2. [Rule 1 - Design refinement] Plan's "replace with empty string" breaks existing wildcard tests**
- **Found during:** Task 2 design analysis (before writing code)
- **Issue:** Plan says `removeRegex.Replace(result, "")` with unanchored regex. For `_{*}new_{*}` on `_foo_new_bar`, this replaces the ENTIRE match with `""` giving `""` instead of `_foo_`. The existing test expects `_foo_.csv`.
- **Fix:** Implemented dual-path strategy: if anchored matchRegex matches → use BuildRemoveReplacement (existing wildcard behavior preserved); else if unanchored removeRegex matches → use empty-string replacement (new literal/substring behavior). Both paths can fire in sequence.
- **Files modified:** `src/FileRevamp/Core/WildcardPatternMatcher.cs`
- **Verification:** All 6 original RenameOrchestratorTests pass, all 3 new WildcardPatternMatcherTests pass
- **Committed in:** `0b38f57` (Task 2 GREEN)

---

**Total deviations:** 2 auto-fixed (1 API mismatch, 1 design conflict resolution)
**Impact on plan:** Both fixes necessary for correctness. No scope creep. All plan requirements delivered.

## Security Mitigations Applied (Threat Model)

- **T-02-01 (Tampering — invalid chars in computed filename):** After all transforms, `RenameOrchestrator` validates `newFilename` against `Path.GetInvalidFileNameChars()`. Any invalid character emits `FailedResult("Computed filename contains invalid characters: '...'")`.
- **T-02-02 (Path traversal in destPath):** Inherited from Plan 01 T-01-01 mitigation — `Path.GetDirectoryName(Path.GetFullPath(destPath)) == normalizedDir` check remains in RenameOrchestrator.
- **T-02-SC (No new packages):** Only existing verified packages used (FileSystemGlobbing already in csproj from Plan 01 scaffold).

## Known Stubs

None. All pipeline components are functional. No placeholder values flow to console output.

## Threat Flags

No new security surface beyond the analyzed threat model.

## TDD Gate Compliance

- RED gate: `32a1109` (test commit for Task 1), `9bcfc8b` (test commit for Task 2)
- GREEN gate: `44593ea` (feat commit for Task 1), `0b38f57` (feat commit for Task 2)
- REFACTOR gate: not needed — code was clean on first pass

## Self-Check: PASSED

- [x] `src/FileRevamp/Core/FileDiscovery.cs` exists
- [x] `src/FileRevamp/Core/ReplaceTransform.cs` exists
- [x] `src/FileRevamp/Core/WildcardCompiler.cs` exists (modified)
- [x] `src/FileRevamp/Core/WildcardPatternMatcher.cs` exists (modified)
- [x] `src/FileRevamp/Core/RenameOrchestrator.cs` exists (modified)
- [x] `src/FileRevamp/Commands/RenameCommand.cs` exists (modified)
- [x] `tests/FileRevamp.Tests/Core/FileDiscoveryTests.cs` exists
- [x] `tests/FileRevamp.Tests/Core/ReplaceTransformTests.cs` exists
- [x] `tests/FileRevamp.Tests/Core/WildcardPatternMatcherTests.cs` exists
- [x] `tests/FileRevamp.Tests/Core/RenameOrchestratorTests.cs` exists (modified)
- [x] Commit 32a1109 exists (RED Task 1)
- [x] Commit 44593ea exists (GREEN Task 1)
- [x] Commit 9bcfc8b exists (RED Task 2)
- [x] Commit 0b38f57 exists (GREEN Task 2)
- [x] `dotnet build FileRevamp.sln` exits 0
- [x] All 37 tests pass (0 failed)
