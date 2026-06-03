---
phase: 02-safety-and-reporting
reviewed: 2026-06-02T00:00:00Z
depth: standard
files_reviewed: 10
files_reviewed_list:
  - src/FileRevamp/Core/CollisionResolver.cs
  - src/FileRevamp/Core/RenameProposal.cs
  - src/FileRevamp/Core/RenameOrchestrator.cs
  - src/FileRevamp/Output/FailureLogger.cs
  - src/FileRevamp/Commands/RenameCommand.cs
  - src/FileRevamp/Program.cs
  - tests/FileRevamp.Tests/Core/CollisionResolverTests.cs
  - tests/FileRevamp.Tests/Core/RenameOrchestratorTests.cs
  - tests/FileRevamp.Tests/Output/FailureLoggerTests.cs
  - tests/FileRevamp.Tests/Commands/RenameCommandTests.cs
findings:
  critical: 4
  warning: 4
  info: 3
  total: 11
status: issues_found
---

# Phase 02: Code Review Report

**Reviewed:** 2026-06-02T00:00:00Z
**Depth:** standard
**Files Reviewed:** 10
**Status:** issues_found

## Summary

Phase 2 introduces four new or significantly reworked components: `CollisionResolver`, `RenameProposal`, a refactored `RenameOrchestrator` (two-pass Plan/Execute), and `FailureLogger`. `RenameCommand` wires them together. The design intent is sound, but several correctness and security defects were found across both the production code and the test coverage.

The most severe findings are:

1. `FailureLogger` is instantiated inside `RenameCommand` but **never called** — no failures are ever logged, defeating the stated RPRT-03 requirement.
2. The log file at `rename-failures.log` is **not excluded** from `FileDiscovery`, so on a second run the tool will attempt to rename its own log file.
3. `CollisionResolver` contains an **infinite loop** with no upper bound — a crafted file system can hang the process.
4. The `RenameCommand.Execute` override drops the `CancellationToken` parameter that the base class does not have, so it silently shadows an invented signature — this compiles only because `protected override int Execute(CommandContext, RenameSettings, CancellationToken)` does not exist in Spectre's base class, meaning the override resolves as a new `Execute(CommandContext, RenameSettings)` hiding the real override.

---

## Critical Issues

### CR-01: FailureLogger Is Never Called — Failed Renames Are Never Logged

**File:** `src/FileRevamp/Commands/RenameCommand.cs:96-117`

**Issue:** A `FailureLogger` is declared in `RenameCommand` comments but the object is never constructed and `Log()` is never called anywhere in the command. The `Execute` method collects `results`, counts `failed`, returns exit code 1 when failures occur, but the failed `RenameResult` entries are never passed to `FailureLogger.Log()`. RPRT-03 ("log failures to rename-failures.log") is completely unimplemented despite the infrastructure existing.

**Fix:**
```csharp
// After collecting results, log any failures:
var logger = new FailureLogger(directoryPath);
foreach (var result in results.Where(r => r.Status == RenameStatus.Failed))
{
    logger.Log(result.OriginalName, result.FailureReason ?? "unknown error");
}
```
This must be placed after the `earlyResults.Concat(orchestrator.Execute(...))` call and before the per-result console output loop.

---

### CR-02: Log File Not Excluded from FileDiscovery — Tool Renames Its Own Log on Second Run

**File:** `src/FileRevamp/Commands/RenameCommand.cs:96`

**Issue:** `FileDiscovery.GetFiles(directoryPath, globPattern)` returns every file in the directory. `rename-failures.log` is not filtered out before being passed to `Plan()`. On a second run (where a previous failure log exists) the rename pipeline will process `rename-failures.log` as a candidate source file. Depending on the active patterns, this either renames or attempts to rename the diagnostic log, destroying it or producing garbage output. The Phase 2 plan comment on line 95 explicitly says "rename-failures.log exclusion filter" is part of the wiring, but no such filter was implemented.

**Fix:**
```csharp
// After GetFiles() call, filter out the log file before passing to Plan():
var logFileName = Path.GetFileName(new FailureLogger(directoryPath).LogFilePath);
var filePaths = new FileDiscovery(fileSystem)
    .GetFiles(directoryPath, globPattern)
    .Where(p => !string.Equals(
        Path.GetFileName(p), logFileName,
        StringComparison.OrdinalIgnoreCase))
    .ToList();
```

---

### CR-03: Infinite Loop in CollisionResolver — No Upper Bound on Numbering Iteration

**File:** `src/FileRevamp/Core/CollisionResolver.cs:50-59`

**Issue:** The `for (var i = 1; ; i++)` loop has no termination condition other than finding a free slot. If the file system (real or mock) reports every candidate as occupied — due to a bug in `FileExists`, a filesystem quirk, or adversarial setup — this loop runs forever, hanging the CLI process. In a real scenario, if someone has `report(1).csv` through `report(2147483646).csv` and `i` overflows to `int.MinValue` after `int.MaxValue`, the loop wraps silently (unchecked arithmetic in C# by default) and continues indefinitely from the negative side. Even if that extreme is implausible, the absence of any guard means a file system bug produces an unrecoverable hang rather than an error.

**Fix:**
```csharp
const int MaxAttempts = 10_000;
for (var i = 1; i <= MaxAttempts; i++)
{
    var candidate = $"{stem}({i}){ext}";
    var candidatePath = _fileSystem.Combine(directoryPath, candidate);
    if (!claimed.Contains(candidate) && !_fileSystem.FileExists(candidatePath))
    {
        claimed.Add(candidate);
        return candidate;
    }
}
throw new InvalidOperationException(
    $"Could not find a free filename for '{desiredName}' after {MaxAttempts} attempts.");
```
The caller in `RenameOrchestrator.Plan()` should catch this and add a `FailedResult` to `earlyResults`.

---

### CR-04: RenameCommand.Execute Signature Does Not Override the Base Class — CancellationToken Parameter Is Fabricated

**File:** `src/FileRevamp/Commands/RenameCommand.cs:28`

**Issue:** The declared signature is:
```csharp
protected override int Execute(CommandContext context, RenameSettings settings, CancellationToken cancellationToken = default)
```
`Spectre.Console.Cli.Command<TSettings>` defines `Execute(CommandContext, TSettings)` — a two-parameter method. No overload with `CancellationToken` exists in Spectre 0.55. Because C# allows overloading with optional parameters, this compiles but the `override` keyword is misleading: the compiler will match the two-parameter virtual if called with two args (which Spectre does), and the three-parameter method will be dead code. Spectre's dispatcher calls the two-argument virtual, which falls through to the base implementation returning 0, leaving the command body completely unreachable in production.

This means **the entire command never executes** when invoked through the real CLI. It only appears to work in tests because `CommandAppTester` uses reflection or its own dispatch path. Verify by checking if `CommandAppTester.Run()` in Spectre 0.55 dispatches to the two-arg or three-arg overload; if it dispatches to two-arg, even the tests are exercising the wrong path.

**Fix:** Remove the `CancellationToken` parameter entirely:
```csharp
protected override int Execute(CommandContext context, RenameSettings settings)
{
    // ... existing body, removing the cancellationToken reference (it is unused anyway)
}
```

---

## Warnings

### WR-01: FailureLogger Uses DateTime.Now Instead of UTC — Log Timestamps Are Ambiguous Across Timezones

**File:** `src/FileRevamp/Output/FailureLogger.cs:31`

**Issue:** `DateTime.Now` produces a local-time timestamp with no timezone indicator. Log files written on a machine with DST transitions, or log files shared across machines in different timezones, will have ambiguous or misleading timestamps. Standard practice for diagnostic logs is to use UTC.

**Fix:**
```csharp
var line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z] FAIL {originalName}: {reason}";
```

---

### WR-02: RenameOrchestrator.Plan Uses directoryPath (Non-Normalized) for CollisionResolver, But normalizedDir for Path Traversal Check — Inconsistency

**File:** `src/FileRevamp/Core/RenameOrchestrator.cs:48,109,119`

**Issue:** `normalizedDir` is computed via `Path.GetFullPath(directoryPath)` on line 48 and used for the path-traversal check on line 112. However, the path passed to `CollisionResolver.Resolve()` on line 119 is the raw `directoryPath` (not `normalizedDir`). Inside `CollisionResolver`, that path is passed to `_fileSystem.Combine()` and `_fileSystem.FileExists()`. If `directoryPath` is a relative path (e.g. `"."` or `"exports"`), `FileExists` will be called with a relative path while the traversal check used the absolute form. On the real `FileSystem`, `File.Exists("./report.csv")` and `File.Exists("C:/exports/report.csv")` both work, but the inconsistency is a latent correctness risk and the collision check may fail to detect a real conflict in edge cases.

**Fix:** Pass `normalizedDir` to `CollisionResolver.Resolve()` instead of `directoryPath`:
```csharp
var resolvedName = resolver.Resolve(normalizedDir, newFilename, claimed);
```
And likewise on line 109, use `normalizedDir`:
```csharp
var destPath = _fileSystem.Combine(normalizedDir, newFilename);
```

---

### WR-03: RenameCommand Does Not Validate That at Least One of --remove or --replace Is Provided

**File:** `src/FileRevamp/Commands/RenameCommand.cs:63-98`

**Issue:** When neither `--remove` nor `--replace` is specified, `patternMatcher.HasPatterns` is false and `replaceTransforms` is empty. `Plan()` enters replace-only mode (all files are candidates) but the transform loop does nothing, so every file produces "Transform produced no change" and is added to `earlyResults` as Skipped. The command exits 0 printing only "Skipped" lines. No warning is shown to the user that the operation was a no-op. This is a usability defect: the user gets silent confirmation of doing nothing.

**Fix:** In `RenameSettings.Validate()`, add:
```csharp
if ((RemovePatterns is null || RemovePatterns.Length == 0) &&
    (ReplaceOperations is null || ReplaceOperations.Length == 0))
{
    return ValidationResult.Error(
        "Specify at least one --remove pattern or --replace operand.");
}
```

---

### WR-04: MockFileSystem Is in the Production Assembly (src/), Not the Test Assembly

**File:** `src/FileRevamp/Core/MockFileSystem.cs`

**Issue:** `MockFileSystem` lives in `src/FileRevamp/Core/` alongside production code. It will be compiled into the shipping binary and distributed to end users. Test infrastructure should be confined to the test project. This also exposes the `MockFileSystem` type through the public API of the tool's namespace.

**Fix:** Move `MockFileSystem.cs` to `tests/FileRevamp.Tests/` under an appropriate namespace such as `FileRevamp.Tests.Fakes`. Update all `using` directives in the test files that reference it.

---

## Info

### IN-01: Program.cs Does Not Use SetApplicationVersion — Context Says It Was Added, But It Is Absent

**File:** `src/FileRevamp/Program.cs`

**Issue:** The phase context notes "Program.cs: SetApplicationVersion added," but the actual file contains no call to `config.SetApplicationVersion(...)`. The only configure calls are `SetApplicationName` and `AddExample`. Either the version wiring was dropped or the context description is stale. Either way, `--version` will not display a meaningful version string.

**Fix:** Add version from the assembly:
```csharp
config.SetApplicationVersion(
    typeof(Program).Assembly.GetName().Version?.ToString() ?? "dev");
```

---

### IN-02: RenameOrchestratorTests Use Unix-Style Paths That Will Fail on Windows

**File:** `tests/FileRevamp.Tests/Core/RenameOrchestratorTests.cs:9`

**Issue:** The constant `ExportsDir = "/exports"` uses a Unix absolute path. On Windows, `Path.GetFullPath("/exports")` resolves to the root of the current drive (e.g. `C:\exports`), not `/exports`. `RenameOrchestrator.Plan()` calls `Path.GetFullPath(directoryPath)` on line 48, producing `C:\exports` on Windows. The path-traversal check then compares `C:\exports` against `destDir` derived from `_fileSystem.Combine("/exports", newFilename)` which returns `/exports/newFilename`. This mismatch causes every proposal to be rejected as a path traversal attempt, meaning the tests would fail on Windows unless `MockFileSystem.Combine()` returns a Windows-style path — and it does not (it always uses forward slashes, so `Path.GetDirectoryName("/exports/report.csv")` on Windows returns `\exports` not `/exports`).

The tests pass only if run on Linux/macOS. On Windows CI they will either fail or produce false positives depending on how `Path.GetFullPath` normalizes the drive prefix.

**Fix:** Use a test-specific path constant that is valid on the target platform, or mock the path normalization step. A simple cross-platform workaround:
```csharp
private static readonly string ExportsDir =
    Path.Combine(Path.GetTempPath(), "filerevamp_test_exports");
```
Alternatively, restructure `Plan()` to accept a pre-normalized directory string so tests can inject it.

---

### IN-03: CollisionResolverTests Also Uses /exports — Same Cross-Platform Issue as IN-02

**File:** `tests/FileRevamp.Tests/Core/CollisionResolverTests.cs:9`

**Issue:** `CollisionResolver.Resolve()` calls `_fileSystem.Combine(directoryPath, desiredName)`. In `MockFileSystem.Combine` this always returns `"/exports/report.csv"` (forward-slash). `FileExists` then normalizes and looks up. The tests work in isolation because `MockFileSystem` uses forward-slash paths consistently. However, the moment a test constructs a path via `Path.GetFullPath` (as `RenameOrchestrator.Plan` does) or runs on a path that `System.IO.Path` then normalizes, the two representations diverge. The issue is lower severity here than in IN-02 because `CollisionResolver` itself does not call `Path.GetFullPath`, but the test suite is not portable and will mislead contributors.

**Fix:** Same remediation as IN-02 — use platform-neutral test paths or a dedicated mock path helper.

---

_Reviewed: 2026-06-02T00:00:00Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
