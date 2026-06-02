# Phase 2: Safety and Reporting - Pattern Map

**Mapped:** 2026-06-01
**Files analyzed:** 5 new/modified files
**Analogs found:** 5 / 5

---

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|---|---|---|---|---|
| `src/FileRevamp/Core/RenameProposal.cs` | model | transform | `src/FileRevamp/Output/RenameResult.cs` | exact |
| `src/FileRevamp/Core/CollisionResolver.cs` | service | transform | `src/FileRevamp/Core/ReplaceTransform.cs` | role-match |
| `src/FileRevamp/Output/FailureLogger.cs` | service | file-I/O | `src/FileRevamp/Output/Reporter.cs` | role-match |
| `src/FileRevamp/Core/RenameOrchestrator.cs` | service | batch | `src/FileRevamp/Core/RenameOrchestrator.cs` (self) | exact |
| `src/FileRevamp/Commands/RenameCommand.cs` | controller | request-response | `src/FileRevamp/Commands/RenameCommand.cs` (self) | exact |

---

## Pattern Assignments

### `src/FileRevamp/Core/RenameProposal.cs` (model, transform)

**Analog:** `src/FileRevamp/Output/RenameResult.cs`

`RenameProposal` is an immutable planning record: the source filename and the collision-resolved destination filename computed during the plan pass. Copy the `record` structure and static factory method style from `RenameResult`.

**Namespace + record declaration pattern** (lines 1–26):
```csharp
namespace FileRevamp.Output;

public enum RenameStatus { Renamed, DryRun, Skipped, Failed }

public record RenameResult(
    string OriginalName,
    string NewName,
    RenameStatus Status,
    string? FailureReason = null)
{
    public static RenameResult DryRunResult(string original, string newName) =>
        new(original, newName, RenameStatus.DryRun);

    public static RenameResult RenamedResult(string original, string newName) =>
        new(original, newName, RenameStatus.Renamed);

    public static RenameResult SkippedResult(string original, string reason) =>
        new(original, original, RenameStatus.Skipped, reason);

    public static RenameResult FailedResult(string original, string reason) =>
        new(original, original, RenameStatus.Failed, reason);
}
```

**What to copy for `RenameProposal`:**
- Use `record` (not `class`) — immutable value object, same as `RenameResult`
- Namespace: `FileRevamp.Core` (it is a Core planning type, not an Output type)
- Properties needed: `SourcePath` (full path), `OriginalName` (bare filename), `ResolvedName` (collision-free destination filename), `WouldChange` (bool — false if transform produced no change so the execute pass can emit a Skip without re-computing)
- No static factory methods needed — `RenameProposal` is always constructed with all known fields during the plan pass; use the primary constructor directly
- No enum needed — `RenameProposal` represents intent only; `RenameResult` carries the outcome

---

### `src/FileRevamp/Core/CollisionResolver.cs` (service, transform)

**Analog:** `src/FileRevamp/Core/ReplaceTransform.cs`

`CollisionResolver` is a pure computation class (no console dependency, no output concern) that takes a desired filename, a `HashSet<string>` of already-claimed batch destinations, and returns a collision-free name. Copy the sealed class + constructor-injected dependency style from `ReplaceTransform`.

**Class declaration + constructor pattern** (lines 12–33):
```csharp
public sealed class ReplaceTransform
{
    public string Find { get; }
    public string Replace { get; }

    public ReplaceTransform(string find, string replace)
    {
        if (string.IsNullOrEmpty(find))
            throw new ArgumentException("Find string must not be empty.", nameof(find));
        Find = find;
        Replace = replace;
    }

    public string Apply(string filename) =>
        filename.Replace(Find, Replace, StringComparison.Ordinal);
```

**What to copy for `CollisionResolver`:**
- `sealed class` — same as every existing Core type
- Constructor takes `IFileSystem fileSystem` (same injection pattern as `RenameOrchestrator`)
- Single public method: `string Resolve(string directoryPath, string desiredName, HashSet<string> claimed)`
- `StringComparer.OrdinalIgnoreCase` for the `claimed` HashSet and all string equality checks — Windows NTFS is case-insensitive (established convention from `MockFileSystem`, line 9: `new(StringComparer.OrdinalIgnoreCase)`)
- Use `Path.GetFileNameWithoutExtension` / `Path.GetExtension` for stem/ext split — same as `WildcardPatternMatcher` (lines 77–78) and `Reporter.ValidateOutputName` (line 78)
- The `claimed` set is passed by reference AND mutated inside `Resolve`: add the resolved name to `claimed` before returning (Pitfall 4 — same pattern as the `_files` dict mutation in `MockFileSystem.MoveFile`, lines 40–42)
- `_fileSystem.Combine(directoryPath, candidate)` for path construction — same pattern as `RenameOrchestrator` (line 112: `var destPath = _fileSystem.Combine(directoryPath, newFilename)`)

**Auto-numbering loop pattern** (from RESEARCH.md code examples):
```csharp
// Fast path — desired name is free
var destPath = _fileSystem.Combine(directoryPath, desiredName);
if (!claimed.Contains(desiredName) && !_fileSystem.FileExists(destPath))
{
    claimed.Add(desiredName);
    return desiredName;
}

// Slow path — find first free slot: file(1).csv, file(2).csv, ...
var stem = Path.GetFileNameWithoutExtension(desiredName);
var ext  = Path.GetExtension(desiredName);

for (var i = 1; ; i++)
{
    var candidate = $"{stem}({i}){ext}";
    var candidatePath = _fileSystem.Combine(directoryPath, candidate);
    if (!claimed.Contains(candidate) && !_fileSystem.FileExists(candidatePath))
    {
        claimed.Add(candidate);
        return candidate;
    }
}
```

**Error handling:** No try/catch — `CollisionResolver` is pure computation. `IFileSystem.FileExists` does not throw on normal usage. The loop is theoretically infinite but bounded in practice by disk capacity (see RESEARCH.md T-04-02 — accepted risk).

---

### `src/FileRevamp/Output/FailureLogger.cs` (service, file-I/O)

**Analog:** `src/FileRevamp/Output/Reporter.cs`

`FailureLogger` lives in the `Output` namespace alongside `Reporter`. It is a pure-output concern: takes failure signals from the command and writes them to disk. Copy the `sealed class`, namespace, and no-console-dependency style from `Reporter`.

**Namespace + class shell pattern** (lines 1–12 of Reporter.cs):
```csharp
namespace FileRevamp.Output;

/// <summary>
/// Formats per-file output lines and end-of-run summary strings.
/// This class is a pure string formatter — it has no console dependency.
/// </summary>
public sealed class Reporter
{
    public string FormatResultLine(RenameResult result) => ...
```

**What to copy for `FailureLogger`:**
- Namespace: `FileRevamp.Output`
- `sealed class` with a single constructor taking `string directoryPath`
- Store `_logFilePath = Path.Combine(directoryPath, "rename-failures.log")` — NOT `_fileSystem.Combine(...)` because `FailureLogger` uses BCL `File.AppendAllText` directly (it is an Output concern, not a Core concern)
- `public string LogFilePath => _logFilePath;` — expose for exclusion filtering in `RenameCommand`
- `public void Log(string originalName, string reason)` — lazy write (creates file only on first call, matching `File.AppendAllText` semantics — Pitfall 6 from RESEARCH.md)
- No constructor-time file creation — `File.AppendAllText` creates the file on first call if it does not exist

**Log write pattern** (BCL, no dependencies):
```csharp
public void Log(string originalName, string reason)
{
    var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] FAIL {originalName}: {reason}";
    File.AppendAllText(_logFilePath, line + Environment.NewLine);
}
```

**What NOT to do:** Do not inject `IFileSystem` into `FailureLogger`. `IFileSystem` has no `AppendAllText` method and is the dry-run seam for rename operations only. `FailureLogger` should always write the real log even in dry-run mode (the log records what failed, not what was renamed). Use BCL `File.AppendAllText` directly.

---

### `src/FileRevamp/Core/RenameOrchestrator.cs` (service, batch) — MODIFIED

**Analog:** `src/FileRevamp/Core/RenameOrchestrator.cs` (self — refactor in place)

The existing orchestrator (155 lines) is refactored from a single-pass lazy `IEnumerable<RenameResult>` pipeline into a two-pass design. The existing constructor and field (`_fileSystem`) are unchanged. The existing `Execute(...)` method is split into `Plan(...)` + `Execute(...)`.

**Existing constructor pattern to keep** (lines 13–20):
```csharp
public sealed class RenameOrchestrator
{
    private readonly IFileSystem _fileSystem;

    public RenameOrchestrator(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }
```

**Existing transform pipeline to move into `Plan()`** (lines 54–130) — the body of the `foreach (var filePath in filePaths)` loop. All transform logic (remove patterns, replace transforms, output name validation, invalid-char check, no-change skip, path traversal check) migrates into the plan pass verbatim. The single change: replace the `FileExists` collision stub (lines 124–130) with a `CollisionResolver.Resolve(...)` call.

**Existing `yield return` pattern to replace** — the plan pass must be eager (`IReadOnlyList<RenameProposal>`), not lazy, because SAFE-01 requires all collisions to be resolved before any `MoveFile` call. Replace `yield return RenameResult.*` in the plan pass body with `proposals.Add(new RenameProposal(...))` or early-return `RenameResult.*` for files that will not be renamed (skip/fail results that do not need a proposal).

**Existing live-run try/catch pattern to copy into `Execute()`** (lines 139–150):
```csharp
// C# does not allow yield in try/catch; capture result separately.
RenameResult moveResult;
try
{
    _fileSystem.MoveFile(filePath, destPath);
    moveResult = RenameResult.RenamedResult(filename, newFilename);
}
catch (Exception ex)
{
    moveResult = RenameResult.FailedResult(filename, ex.Message);
}

yield return moveResult;
```

**New `Plan()` method signature** (matches RESEARCH.md architecture sketch):
```csharp
public (IReadOnlyList<RenameProposal> Proposals, IReadOnlyList<RenameResult> EarlyResults)
    Plan(
        IReadOnlyList<string> filePaths,
        WildcardPatternMatcher patternMatcher,
        IReadOnlyList<ReplaceTransform> replaceTransforms,
        string directoryPath)
```

Returning a tuple allows the plan pass to emit both the proposals (files that will be renamed) and the early results (files skipped/failed during planning) to the command in a single call, so the command can report everything in order.

**New `Execute()` method signature:**
```csharp
public IReadOnlyList<RenameResult> Execute(
    IReadOnlyList<RenameProposal> proposals,
    string directoryPath,
    bool dryRun)
```

**Path normalization — keep at entry** (line 49): `var normalizedDir = Path.GetFullPath(directoryPath)` — stays in `Plan()`, not `Execute()` (plan validates; execute trusts the plan).

**FileDiscovery call — move to `RenameCommand`** (line 52): Per the RESEARCH.md architecture diagram, the command resolves `filePaths` and passes them to `Plan()`. Moving `FileDiscovery` to the command also makes the log-file exclusion filter easier to apply (Pitfall 3 — filter after discovery, before `Plan()`).

---

### `src/FileRevamp/Commands/RenameCommand.cs` (controller, request-response) — MODIFIED

**Analog:** `src/FileRevamp/Commands/RenameCommand.cs` (self — targeted additions)

Three targeted changes to the existing `Execute` method; the rest of the file is unchanged.

**1. FileDiscovery call + log exclusion filter** — add after directory validation (after line 56), before orchestrator call. Pattern from RESEARCH.md Pitfall 3:
```csharp
const string LogFileName = "rename-failures.log";
var filePaths = new FileDiscovery(fileSystem)
    .GetFiles(directoryPath, globPattern)
    .Where(p => !string.Equals(
        System.IO.Path.GetFileName(p), LogFileName,
        StringComparison.OrdinalIgnoreCase))
    .ToList();
```

**2. FailureLogger instantiation + wiring** — add before the orchestrator call, after `filePaths` is built:
```csharp
var failureLogger = new FailureLogger(directoryPath);
```

After the execute pass, iterate results and call `failureLogger.Log(...)` for each `RenameStatus.Failed` result (the orchestrator returns results; the command decides how to log them — same separation as `reporter.FormatResultLine`).

**3. Two-pass orchestrator call** — replace the existing single-call (line 96–97):
```csharp
// Old (Phase 1):
var results = orchestrator.Execute(directoryPath, globPattern, patternMatcher, replaceTransforms, settings.DryRun)
    .ToList();

// New (Phase 2):
var (proposals, earlyResults) = orchestrator.Plan(filePaths, patternMatcher, replaceTransforms, directoryPath);
var executeResults = orchestrator.Execute(proposals, directoryPath, settings.DryRun);
var results = earlyResults.Concat(executeResults).ToList();
```

**Existing patterns to keep unchanged:**
- `IAnsiConsole _console` injection (lines 15–25) — no change
- `Markup.Escape(reporter.FormatResultLine(result))` loop (lines 99–104) — no change
- `reporter.FormatSummary` / `reporter.FormatDryRunComplete` calls (lines 106–113) — no change
- Exit code logic based on `results.Count(r => r.Status == RenameStatus.Failed)` (lines 115–116) — no change

**`Program.cs` UX-01 change** — add `config.SetApplicationVersion("1.0.0")` inside the `app.Configure(config => { ... })` block (line 5 of Program.cs). No other changes to `Program.cs`. The two existing `AddExample` calls already satisfy UX-01's wildcard and replace example requirements.

---

## Shared Patterns

### `sealed class` + single-field constructor

**Source:** Every existing Core and Output type (`RenameOrchestrator`, `Reporter`, `WildcardPatternMatcher`, `ReplaceTransform`, `MockFileSystem`)
**Apply to:** `CollisionResolver`, `FailureLogger`, `RenameProposal`

```csharp
public sealed class CollisionResolver
{
    private readonly IFileSystem _fileSystem;

    public CollisionResolver(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }
```

All types in this project are `sealed`. No inheritance, no virtual members.

### `record` for immutable value objects

**Source:** `src/FileRevamp/Output/RenameResult.cs` lines 21–42
**Apply to:** `RenameProposal`

Positional `record` with primary constructor. All properties are init-only by default. No mutable state.

### `StringComparer.OrdinalIgnoreCase` for Windows path/filename sets

**Source:** `src/FileRevamp/Core/MockFileSystem.cs` line 9
**Apply to:** `CollisionResolver` (the `claimed` HashSet parameter), any filename equality checks

```csharp
private readonly Dictionary<string, bool> _files =
    new(StringComparer.OrdinalIgnoreCase);
```

`CollisionResolver` receives `HashSet<string>` from the caller. The caller (plan pass in `RenameOrchestrator`) must construct it as:
```csharp
var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
```

### `Path.GetFileNameWithoutExtension` / `Path.GetExtension` for stem/ext splitting

**Source:** `src/FileRevamp/Core/WildcardPatternMatcher.cs` lines 77–78; `src/FileRevamp/Output/Reporter.cs` line 78
**Apply to:** `CollisionResolver` (stem/ext split for the `file(N)` numbering suffix)

```csharp
var extension = Path.GetExtension(normalizedFilename);   // includes leading dot, or "" if none
var stem = Path.GetFileNameWithoutExtension(normalizedFilename);
```

### `_fileSystem.Combine(dir, filename)` for path construction in Core

**Source:** `src/FileRevamp/Core/RenameOrchestrator.cs` line 112
**Apply to:** `CollisionResolver`

```csharp
var destPath = _fileSystem.Combine(directoryPath, newFilename);
```

Do NOT use `System.IO.Path.Combine` directly in Core types — the `IFileSystem.Combine` abstraction keeps tests working with mock paths (Unix-style `/exports/filename.csv` in MockFileSystem).

### No-console Core types

**Source:** `src/FileRevamp/Core/RenameOrchestrator.cs`, `WildcardPatternMatcher.cs`, `ReplaceTransform.cs`
**Apply to:** `CollisionResolver`, `RenameOrchestrator` (plan pass)

Core types have zero dependency on `Spectre.Console` or `IAnsiConsole`. They return data structures; the command (controller) formats and emits output.

### xUnit + FluentAssertions test structure

**Source:** `tests/FileRevamp.Tests/Core/RenameOrchestratorTests.cs`
**Apply to:** `CollisionResolverTests`, `FailureLoggerTests`, updated `RenameOrchestratorTests`

```csharp
using FileRevamp.Core;
using FileRevamp.Output;
using FluentAssertions;

namespace FileRevamp.Tests.Core;

public class CollisionResolverTests
{
    private const string ExportsDir = "/exports";

    [Fact]
    public void Resolve_DesiredNameFree_ReturnsSameName_AndAddsToClaimed()
    {
        // Arrange
        var fs = new MockFileSystem(Array.Empty<string>());
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolver = new CollisionResolver(fs);

        // Act
        var result = resolver.Resolve(ExportsDir, "report.csv", claimed);

        // Assert
        result.Should().Be("report.csv");
        claimed.Should().Contain("report.csv");
    }
```

Test naming convention: `{Method}_{Scenario}_{ExpectedOutcome}` — e.g., `Resolve_DesiredNameFree_ReturnsSameName_AndAddsToClaimed`.

### `IDisposable` + temp dir cleanup for command tests

**Source:** `tests/FileRevamp.Tests/Commands/RenameCommandTests.cs` lines 14–26
**Apply to:** New command-level tests for Phase 2 (collision dry-run, log file creation)

```csharp
public sealed class RenameCommandTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    private string CreateTempDir(params string[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"filerevamp_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);

        foreach (var file in files)
            File.WriteAllText(Path.Combine(dir, file), string.Empty);

        return dir;
    }
```

### `FakeTypeRegistrar` + `CommandAppTester` wiring

**Source:** `tests/FileRevamp.Tests/Commands/RenameCommandTests.cs` lines 33–50
**Apply to:** Any new CLI-level tests that need captured console output

```csharp
private static CommandAppTester CreateTester()
{
    var tester = new CommandAppTester();
    var registrar = new FakeTypeRegistrar();
    registrar.RegisterInstance(typeof(IAnsiConsole), tester.Console);
    registrar.RegisterInstance(typeof(RenameCommand), new RenameCommand(tester.Console));
    tester.Registrar = registrar;
    tester.SetDefaultCommand<RenameCommand>("Rename files in a directory");
    tester.Configure(config =>
    {
        config.SetApplicationName("filerevamp");
    });
    return tester;
}
```

---

## No Analog Found

All five files have close analogs in the existing codebase. No file requires research-only patterns.

---

## Metadata

**Analog search scope:** `src/FileRevamp/` (all .cs), `tests/FileRevamp.Tests/` (all .cs)
**Files scanned:** 17 source files, 6 test files
**Pattern extraction date:** 2026-06-01
