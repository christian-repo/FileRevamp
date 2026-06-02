---
phase: 01-core-rename-pipeline
fixed_at: 2026-06-01T00:00:00Z
review_path: .planning/phases/01-core-rename-pipeline/01-REVIEW.md
iteration: 1
findings_in_scope: 8
fixed: 8
skipped: 0
status: all_fixed
---

# Phase 01: Code Review Fix Report

**Fixed at:** 2026-06-01
**Source review:** .planning/phases/01-core-rename-pipeline/01-REVIEW.md
**Iteration:** 1

**Summary:**
- Findings in scope: 8 (CR-01, CR-02, CR-03, WR-01, WR-02, WR-03, WR-04, WR-05)
- Fixed: 8
- Skipped: 0

Note: WR-04 was applied in the same commit as CR-03 because both fixes modify `ValidateOutputName`
in `Reporter.cs`. The combined commit is `a516af1`.

## Fixed Issues

### CR-01: Invalid `--replace` operand silently discarded — exit code stays 0

**Files modified:** `src/FileRevamp/Commands/RenameCommand.cs`
**Commit:** 38eaa20
**Applied fix:** Added `replaceParseError` boolean flag in the `--replace` parsing block. When any
`ArgumentException` is caught, the flag is set to `true`. After the LINQ chain completes, if the
flag is set, the command returns exit code `1` immediately before processing any files.

---

### CR-02: Greedy wildcard produces counterintuitive remove behavior for repeated literals

**Files modified:** `src/FileRevamp/Core/WildcardCompiler.cs`
**Commit:** fff3751
**Applied fix:** Changed wildcard quantifiers from greedy to non-greedy in `ToRegex`:
- `(.*)` → `(.*?)`
- `(.+)` → `(.+?)`

The `(.?)` token was unchanged (zero-or-one; greedy vs non-greedy is equivalent for single-character
quantifiers). Added a code comment explaining the behavioral rationale (first literal occurrence
anchors the removal). The `BuildRemoveReplacement` doc comment already described `(.*?)` — the
fix makes the implementation match the documentation.

---

### CR-03: Empty output stem produces dotfile (e.g., `.csv`) without error

**Files modified:** `src/FileRevamp/Output/Reporter.cs`
**Commit:** a516af1
**Applied fix:** Added an empty-stem check in `ValidateOutputName` after the existing trailing-dot
and trailing-space checks. Uses `Path.GetFileNameWithoutExtension(filename)` and returns a
descriptive error if the stem is null or empty.

Note: WR-04 was also applied in this same commit (see below).

---

### WR-01: `TypeRegistrar.RegisterLazy` evaluates the factory eagerly

**Files modified:** `src/FileRevamp/Infrastructure/TypeRegistrar.cs`
**Commit:** fcaa381
**Applied fix:** Added a `_lazyInstances` dictionary of type `Dictionary<Type, Lazy<object>>` to
`TypeRegistrar`. `RegisterLazy` now stores `new Lazy<object>(factory)` instead of calling
`factory()` immediately. `TypeResolver` was updated to accept `_lazyInstances` in its constructor
and to check it during `Resolve()`, returning `lazy.Value` (which triggers evaluation at most once)
before checking `_registrations`.

---

### WR-02: Literal pattern matching full stem returns extension-only filename instead of null

**Files modified:** `src/FileRevamp/Core/WildcardPatternMatcher.cs`,
`tests/FileRevamp.Tests/Core/WildcardPatternMatcherTests.cs`
**Commit:** 3114b23
**Applied fix:** Added a guard immediately after `matchRegex.Replace` in the anchored-match branch:
if the replacement result has zero length and the extension is non-empty, `ApplyRemoves` returns
`null` instead of the extension-only string. This is the correct semantic — a pattern that would
produce an inaccessible filename is treated as a non-match, and the orchestrator skips the file.

The existing NFC normalization test (`ApplyRemoves_NfdFilename_MatchesNfcPattern`) was updated to
use a pattern that removes only a prefix (leaving a non-empty stem) so it continues to verify NFC
normalization without relying on the now-rejected empty-stem behavior. A new test
`ApplyRemoves_FullStemErasure_ReturnsNull` was added to cover the guarded case directly.

---

### WR-03: `ReplaceTransform` constructor does not validate that `Find` is non-empty

**Files modified:** `src/FileRevamp/Core/ReplaceTransform.cs`
**Commit:** 5ca716c
**Applied fix:** Added a `string.IsNullOrEmpty(find)` guard in the constructor that throws
`ArgumentException("Find string must not be empty.", nameof(find))`. Also added an equivalent
guard in `Parse` that checks `find.Length == 0` after splitting on `->` and throws with a message
that includes the expected format.

---

### WR-04: `ValidateOutputName` does not check for Windows reserved device names

**Files modified:** `src/FileRevamp/Output/Reporter.cs`
**Commit:** a516af1 (combined with CR-03)
**Applied fix:** Added a `private static readonly HashSet<string> WindowsReservedNames` containing
`CON`, `PRN`, `AUX`, `NUL`, `COM1`–`COM9`, and `LPT1`–`LPT9` (case-insensitive).
`ValidateOutputName` extracts the stem via `Path.GetFileNameWithoutExtension` and checks it against
the set, returning a descriptive error if matched. The check runs after the empty-stem check so
the stem is guaranteed non-empty.

---

### WR-05: Dry-run mode provides no summary count of files that would be renamed

**Files modified:** `src/FileRevamp/Output/Reporter.cs`,
`src/FileRevamp/Commands/RenameCommand.cs`,
`tests/FileRevamp.Tests/Output/ReporterTests.cs`,
`tests/FileRevamp.Tests/Commands/RenameCommandTests.cs`
**Commit:** 6d4d6a3
**Applied fix:** Changed `FormatDryRunComplete()` signature to `FormatDryRunComplete(IEnumerable<RenameResult> results)`.
The method now counts `DryRun`, `Skipped`, and `Failed` results and returns a message like:
`"Dry run complete — N would be renamed, M skipped, K failed. 0 files modified."`
Updated the call site in `RenameCommand.Execute` to pass the `results` list.
Updated `ReporterTests` (split old test into two — zero-results and with-results) and updated
`RenameCommandTests` to match the new message format.

## Skipped Issues

None — all 8 in-scope findings were fixed.

---

_Fixed: 2026-06-01_
_Fixer: Claude (gsd-code-fixer)_
_Iteration: 1_
