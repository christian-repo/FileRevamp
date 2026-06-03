---
phase: 03-polish-and-packaging
reviewed: 2026-06-03T00:00:00Z
depth: standard
files_reviewed: 11
files_reviewed_list:
  - src/FileRevamp/Commands/RenameCommand.cs
  - src/FileRevamp/Commands/RenameSettings.cs
  - src/FileRevamp/FileRevamp.csproj
  - src/FileRevamp/Output/FailureLogger.cs
  - src/FileRevamp/Program.cs
  - tests/FileRevamp.Tests/Commands/EdgeCaseIntegrationTests.cs
  - tests/FileRevamp.Tests/Core/CollisionResolverTests.cs
  - tests/FileRevamp.Tests/Core/FileDiscoveryTests.cs
  - tests/FileRevamp.Tests/Core/RenameOrchestratorTests.cs
  - tests/FileRevamp.Tests/Fakes/MockFileSystem.cs
  - tests/FileRevamp.Tests/Output/FailureLoggerTests.cs
findings:
  critical: 1
  warning: 5
  info: 1
  total: 7
status: issues_found
---

# Phase 03: Code Review Report

**Reviewed:** 2026-06-03T00:00:00Z
**Depth:** standard
**Files Reviewed:** 11
**Status:** issues_found

## Summary

All 11 source files were read in full. The core rename pipeline (WildcardCompiler, WildcardPatternMatcher, ReplaceTransform, RenameOrchestrator, CollisionResolver, Reporter, FailureLogger) is well-structured and the 79 existing tests all pass. The two-pass plan/execute design, the Markup.Escape wrapping, and the path-traversal guards are correctly implemented.

One blocker was found: `CollisionResolver` checks `FileExists` during planning, which causes false-positive collisions when a source file's name happens to be the same as a computed destination for a different file in the same batch. This produces silent wrong-output (files get auto-numbered when they should not) with no warning to the user.

Five additional warnings were found: unguarded `File.AppendAllText` in `FailureLogger`, a behavioral inconsistency between case-insensitive `--remove` and case-sensitive `--replace`, an incomplete null-guard in `WildcardPatternMatcher.ApplyRemoves`, a dead `searchPattern` parameter on `IFileSystem.GetFiles`, and a corresponding `MockFileSystem` that silently ignores pattern filtering.

## Narrative Findings (AI reviewer)

## Critical Issues

### CR-01: `CollisionResolver` produces false-positive collisions for source-file destination names

**File:** `src/FileRevamp/Core/CollisionResolver.cs:39-44`

**Issue:** During `Plan()`, `CollisionResolver.Resolve` calls `_fileSystem.FileExists(destPath)` to detect on-disk conflicts. If another file in the batch happens to share the same name as a computed destination, `FileExists` returns `true` for that *source* file (which has not yet been renamed), triggering unnecessary auto-numbering.

Concrete example: directory contains `a.csv` and `b.csv`. The user runs `--replace a->b`. Processing:
1. `b.csv` computes to `b.csv` (no change) — put in earlyResults as Skipped ("Transform produced no change").
2. `a.csv` computes to `b.csv`. `CollisionResolver.Resolve(dir, "b.csv", claimed)` checks `FileExists(dir/b.csv)` → **true** (because `b.csv` is still on disk as a source file). Returns `b(1).csv` instead of `b.csv`.

Result: `a.csv` is silently renamed to `b(1).csv` when the user expected `b.csv`. No warning is issued. The behavior also occurs when the destination name matches a source file that is being renamed away in the same batch — the resolver does not account for the fact that those source files will vacate their names during Execute.

This is a correctness failure: silent wrong output on a common usage pattern (renaming a file to the name of another file being processed in the same batch).

**Fix:** Seed the `claimed` set with all *source* filenames before the planning loop begins. This prevents the resolver from treating still-present source files as occupied destinations:

```csharp
// In RenameOrchestrator.Plan(), before the foreach loop:
var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

// Seed claimed with all source filenames so CollisionResolver does not
// false-positive against source files that will vacate during Execute.
foreach (var filePath in filePaths)
    claimed.Add(_fileSystem.GetFileName(filePath));

var resolver = new CollisionResolver(_fileSystem);
```

Note: after this fix, `claimed` now tracks both "names that will be vacated (source files)" and "names that will be occupied (resolved destinations)". The fast-path in `CollisionResolver.Resolve` must also skip the `claimed.Contains(desiredName)` check when the name is a source filename — or alternatively, use two separate sets (sourceNames and claimedDestinations) so the resolver can distinguish source vacating from destination claiming. The simplest correct approach is to seed claimed with source names, then have `Resolve` remove source-name entries from `claimed` as each source is resolved (mark it as "about to vacate"), or check against a separate destinationsClaimed set only.

The most correct minimal fix is a separate `destinationsClaimed` set passed to the resolver:

```csharp
var sourceFolding = new HashSet<string>(
    filePaths.Select(p => _fileSystem.GetFileName(p)),
    StringComparer.OrdinalIgnoreCase);

var destinationsClaimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var resolver = new CollisionResolver(_fileSystem, sourceFolding);
```

And update `CollisionResolver.Resolve` to treat a name occupied only by a source file (not a pre-existing non-source file) as free.

---

## Warnings

### WR-01: `FailureLogger.Log` has no exception handling — crash after successful renames

**File:** `src/FileRevamp/Output/FailureLogger.cs:32`

**Issue:** `File.AppendAllText(_logFilePath, line + Environment.NewLine)` can throw `IOException`, `UnauthorizedAccessException`, or `DirectoryNotFoundException`. This exception is unhandled in `FailureLogger.Log` and in the call site in `RenameCommand` (lines 116-119). If the log write fails — for example because the directory became read-only between the rename operations and the log write — the process crashes with an unhandled exception. The user's files will have been successfully renamed, but the process exits with an unhandled CLR exception rather than exit code 1 plus an informative message.

**Fix:** Wrap the log write in a try-catch and emit a console warning instead of crashing:

```csharp
public void Log(string originalName, string reason)
{
    var line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z] FAIL {originalName}: {reason}";
    try
    {
        File.AppendAllText(_logFilePath, line + Environment.NewLine);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        // Log write failed — degrade gracefully rather than crash.
        // Caller should surface this warning to the user.
        throw new FailureLogWriteException(_logFilePath, ex);
    }
}
```

Or, more simply, have `RenameCommand` wrap the call:

```csharp
foreach (var result in results.Where(r => r.Status == RenameStatus.Failed))
{
    try
    {
        failureLogger.Log(result.OriginalName, result.FailureReason ?? "Unknown error");
    }
    catch (Exception ex)
    {
        _console.MarkupLine(
            $"[yellow]Warning: could not write to failure log: {Markup.Escape(ex.Message)}[/]");
    }
}
```

---

### WR-02: `--remove` is case-insensitive; `--replace` is case-sensitive — inconsistency is undocumented in help text

**File:** `src/FileRevamp/Commands/RenameSettings.cs:22-24` and `src/FileRevamp/Core/ReplaceTransform.cs:46`

**Issue:** Remove patterns are compiled with `RegexOptions.IgnoreCase` (WildcardCompiler line 58), making `--remove _NEW` match `_new` in a filename. Replace transforms use `StringComparison.Ordinal` (ReplaceTransform line 46), making `--replace NEW->old` NOT match `_new_` in a filename. The XML doc comment on `ReplaceTransform.Apply` documents "ordinal (case-sensitive)" but the `[Description]` attribute shown in `--help` output (RenameSettings line 23) contains no such caveat.

A user running both `--remove` and `--replace` operations will observe different case behavior between the two and may have difficulty understanding why `--replace` fails to match when `--remove` succeeds. This is particularly misleading for a Windows-first tool where users expect case-insensitive file operations.

**Fix (minimal):** Update the `--replace` option description to state the case-sensitive behavior:

```csharp
[Description("Replace operation in the form old->new (e.g. .->- replaces dots with dashes). " +
             "The find string is case-sensitive. Can be specified multiple times. Applied after all --remove operations.")]
```

**Fix (ideal):** Make `ReplaceTransform.Apply` case-insensitive to match `--remove` behavior on Windows:

```csharp
public string Apply(string filename) =>
    filename.Replace(Find, Replace, StringComparison.OrdinalIgnoreCase);
```

---

### WR-03: `WildcardPatternMatcher.ApplyRemoves` empty-stem guard covers only the anchored-match path

**File:** `src/FileRevamp/Core/WildcardPatternMatcher.cs:99-113`

**Issue:** The guard at lines 102-103 prevents returning an extension-only filename (e.g., `.csv`) when the anchored `matchRegex` path reduces the stem to empty. However, the unanchored `removeRegex` path at lines 109-111 performs no equivalent guard. If `removeRegex.Replace(currentStem, string.Empty)` reduces `currentStem` to `""`, the method proceeds normally and eventually returns `"" + extension`, producing an extension-only filename like `.csv`.

This is not a data-loss bug — `Reporter.ValidateOutputName` downstream at `RenameOrchestrator.Plan` line 83 will catch the empty stem and produce a `FailedResult`. However, the comment block at lines 99-103 ("If the anchored replacement consumed the entire stem... Treat this as a non-match so the orchestrator skips the file rather than producing an extension-only name") implies this is the *sole* protection. That claim is incorrect: the protection only applies to the anchored path. The inconsistency between the two paths means that a literal-pattern removal that empties the stem takes a different code path (returns extension-only string → FailedResult) than a wildcard-pattern removal (returns null → SkippedResult). This inconsistency affects the user-visible output: literal full-stem removal is reported as FAIL, while wildcard full-stem removal is reported as SKIP.

**Fix:** Add the same guard after the unanchored-replace path:

```csharp
else if (removeRegex.IsMatch(currentStem))
{
    var afterRemove = removeRegex.Replace(currentStem, string.Empty);
    // Guard: avoid extension-only result from unanchored removal too.
    if (afterRemove.Length == 0 && extension.Length > 0)
        return null;
    currentStem = afterRemove;
    anyMatch = true;
}
```

---

### WR-04: `IFileSystem.GetFiles` `searchPattern` parameter is never used with a non-`"*"` value

**File:** `src/FileRevamp/Core/FileDiscovery.cs:43,48` and `src/FileRevamp/Core/IFileSystem.cs:10`

**Issue:** `FileDiscovery.GetFiles` always calls `_fileSystem.GetFiles(directoryPath, "*")` — on line 43 (the early-return path) and on line 48 (the glob-filtered path). The `searchPattern` argument on `IFileSystem.GetFiles` is populated in both `FileSystem` and `DryRunFileSystem` implementations and is passed to `Directory.GetFiles`, but the actual value supplied by `FileDiscovery` is always `"*"`. The parameter exists in the interface contract but is unreachable with a non-`"*"` value through the normal call path.

This misleads future developers who may implement a custom `IFileSystem` and believe `searchPattern` will carry meaningful filtering (it never will unless `FileDiscovery` is changed). It also creates dead coverage: no test exercises `FileSystem.GetFiles` with a non-`"*"` pattern through the `FileDiscovery` path.

**Fix (minimal):** Remove the `searchPattern` parameter from `IFileSystem.GetFiles` and the three implementations since the filtering responsibility belongs entirely to `FileDiscovery` via `Matcher`:

```csharp
// IFileSystem
IEnumerable<string> GetFiles(string directoryPath);

// FileSystem
public IEnumerable<string> GetFiles(string directoryPath) =>
    Directory.GetFiles(directoryPath, "*", SearchOption.TopDirectoryOnly);
```

Update `FileDiscovery` call sites accordingly. `MockFileSystem` should also be updated.

---

### WR-05: `MockFileSystem.GetFiles` silently ignores `searchPattern` — future tests will not catch filtering regressions

**File:** `tests/FileRevamp.Tests/Fakes/MockFileSystem.cs:22-35`

**Issue:** `MockFileSystem.GetFiles(directoryPath, searchPattern)` ignores the `searchPattern` parameter and returns all files in the directory. The current call path in `FileDiscovery` always supplies `"*"`, so this is harmless today. However, if a future test passes a non-`"*"` pattern directly to `MockFileSystem.GetFiles` (expecting it to filter), it will silently receive all files. No assertion will fail — the test would pass with incorrect behavior, providing false confidence. This is a test-reliability defect.

**Fix:** Since `IFileSystem.GetFiles` is always called with `"*"` from `FileDiscovery`, the cleanest fix is to remove `searchPattern` from the interface (see WR-04). If the parameter is kept, add an assertion in `MockFileSystem` to enforce the current contract:

```csharp
public IEnumerable<string> GetFiles(string directoryPath, string searchPattern)
{
    if (searchPattern != "*")
        throw new NotSupportedException(
            $"MockFileSystem.GetFiles only supports '*' as searchPattern; got '{searchPattern}'");
    // ... existing filter logic
}
```

---

## Info

### IN-01: `UnitTest1.cs` placeholder file is unused

**File:** `tests/FileRevamp.Tests/UnitTest1.cs`

**Issue:** The file `tests/FileRevamp.Tests/UnitTest1.cs` is the default xUnit template placeholder. It contributes no tests and adds noise to the test project.

**Fix:** Delete the file. It provides no value and will accumulate in code history unnecessarily.

---

_Reviewed: 2026-06-03T00:00:00Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
