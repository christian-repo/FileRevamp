# Architecture Patterns

**Domain:** .NET C# batch file rename CLI tool
**Project:** FileRevamp
**Researched:** 2026-05-31

---

## Recommended Architecture

A four-layer pipeline with a thin CLI entry point, a pure domain core, a file system adapter, and a console reporter. No framework beyond the BCL and System.CommandLine is required.

```
┌─────────────────────────────────────────────────────┐
│  CLI Layer (Program.cs + Command definitions)        │
│  System.CommandLine 2.0.8                            │
│  Parses args → builds RenameRequest → invokes engine │
└────────────────────┬────────────────────────────────┘
                     │ RenameRequest (plain C# object)
┌────────────────────▼────────────────────────────────┐
│  Pattern Engine                                      │
│  Translates wildcard / regex strings →               │
│    IPatternMatcher + ITransformOperation[]           │
│  Produces: ordered list of transforms                │
└────────────────────┬────────────────────────────────┘
                     │ IPatternMatcher, ITransformOperation[]
┌────────────────────▼────────────────────────────────┐
│  Rename Engine (RenameOrchestrator)                  │
│  Resolves candidate files via IFileScanner           │
│  Applies transforms in fixed order (remove → replace)│
│  Calls IConflictResolver for name collisions         │
│  Calls IFileSystem.File.Move (or no-op for dry run)  │
│  Collects RenameResult per file                      │
└────────────────────┬────────────────────────────────┘
                     │ IEnumerable<RenameResult>
┌────────────────────▼────────────────────────────────┐
│  Reporter                                            │
│  Writes per-file lines to IConsole                   │
│  Writes failure log via IFileSystem                  │
│  Writes summary line (success count / failure count) │
└─────────────────────────────────────────────────────┘
```

---

## Component Boundaries

| Component | Responsibility | Inputs | Outputs | Depends On |
|-----------|---------------|--------|---------|-----------|
| **CLI Layer** | Parse argv; validate required args; construct `RenameRequest`; wire DI container | `string[] args` | `RenameRequest` | System.CommandLine |
| **Pattern Engine** | Translate user-facing pattern strings into executable matcher/transform objects | Pattern strings (wildcard or regex), `--advanced` flag | `IPatternMatcher`, `ITransformOperation[]` | BCL `Regex`, custom wildcard compiler |
| **File Scanner** (`IFileScanner`) | Enumerate candidate files in a directory, respecting `--recursive` | Directory path, `IFileSystem` | `IEnumerable<FileEntry>` | `IFileSystem`, Microsoft.Extensions.FileSystemGlobbing (optional, or BCL `Directory.GetFiles`) |
| **Rename Orchestrator** | Drive the per-file pipeline: scan → transform → conflict-check → execute or dry-run | `RenameRequest`, `IPatternMatcher`, `ITransformOperation[]`, `IFileSystem`, `IConflictResolver`, `bool isDryRun` | `IEnumerable<RenameResult>` | All below interfaces |
| **Conflict Resolver** (`IConflictResolver`) | Given a desired destination path, return a safe non-colliding path using `file(N).ext` pattern | Desired path, `IFileSystem` | Safe path string | `IFileSystem` |
| **File System Adapter** (`IFileSystem`) | Wrap `System.IO` for injectability; enables both dry-run (no-op impl) and unit testing (in-memory impl) | Standard IO calls | Standard results | `System.IO.Abstractions` NuGet or hand-rolled |
| **Reporter** | Format and write all output (per-file progress, failure log, final summary) | `IEnumerable<RenameResult>`, `IConsole`, `IFileSystem` | Console lines, optional log file | `IFileSystem`, `IConsole` |

### What Talks to What

```
CLI Layer
  └─► Pattern Engine           (constructs matchers/transforms from raw strings)
  └─► Rename Orchestrator      (passes request + constructed objects)
        └─► File Scanner        (enumerates files)
        └─► Pattern Engine      (applies transforms to each filename string)
        └─► Conflict Resolver   (checks/resolves destination collisions)
        └─► IFileSystem         (executes or skips the actual File.Move)
  └─► Reporter                 (receives results, writes output)
        └─► IFileSystem         (writes failure log file)
        └─► IConsole            (writes progress + summary lines)
```

No component reaches backward through the stack. The CLI layer is the sole assembly point.

---

## Data Flow: Full Rename Command

```
1. User runs:
   filerevamp ./exports --remove "_{*}new_" --replace ". -> -" --dry-run

2. CLI Layer
   Parses: directory="./exports", removes=["_{*}new_"], replacements=[".→-"], dryRun=true
   Builds: RenameRequest { Directory, RemovePatterns, Replacements, IsDryRun, IsRecursive }

3. Pattern Engine
   For each remove pattern: wildcard compiler converts "_{*}new_" → Regex("_.*new_")
   For each replace operation: builds ReplaceTransform(".", "-")
   Returns: IPatternMatcher (match/strip), ITransformOperation[] in fixed order

4. Rename Orchestrator
   a. IFileScanner.Enumerate(directory, recursive) → FileEntry[]
   b. For each FileEntry:
      i.   Apply IPatternMatcher.Transform(filename) → candidate name
      ii.  Apply each ITransformOperation.Apply(candidate) in order → final name
      iii. If name unchanged → skip (emit RenameResult.Skipped)
      iv.  IConflictResolver.Resolve(finalPath, fileSystem) → safe path
      v.   IsDryRun ?
             DryRun  → emit RenameResult.WouldRename(source, safe path)
             Live    → IFileSystem.File.Move(source, safe path)
                       → emit RenameResult.Renamed or RenameResult.Failed(reason)

5. Reporter
   For each RenameResult: write one line to IConsole
   If any failures: write log file to target directory via IFileSystem
   Write summary: "Renamed: 42  Failed: 1"
```

The `IsDryRun` flag is resolved once at the orchestrator boundary. No conditional branching exists inside the transform or conflict-resolution logic.

---

## Separating Wildcard/Glob Parsing from Full Regex Mode

Use a single `IPatternMatcher` interface. Behind it, two concrete implementations are injected by the CLI layer based on the `--advanced` flag:

```csharp
// Shared interface — same contract regardless of syntax mode
public interface IPatternMatcher
{
    // Returns the portion of filename NOT matched by the remove pattern,
    // or null if no match (file should be skipped).
    string? ApplyRemove(string filename);
}

// Wildcard implementation — compiled once from wildcard syntax
// "_{*}new_" compiles to Regex(  _.*new_  ) internally
public sealed class WildcardPatternMatcher : IPatternMatcher { ... }

// Regex implementation — user supplies raw regex string
// Used when --advanced flag is present
public sealed class RegexPatternMatcher : IPatternMatcher { ... }
```

The wildcard compiler lives in a single static helper `WildcardCompiler.ToRegex(pattern)`:

| Wildcard token | Compiled regex |
|---------------|----------------|
| `{*}` | `.*` (zero or more chars) |
| `{+}` | `.+` (one or more chars) |
| `{?}` | `.?` (zero or one char) |
| All other chars | `Regex.Escape(char)` |

This keeps the compilation logic isolated, testable in pure unit tests with no file system, and replaceable without touching the orchestrator.

---

## Dry-Run vs Live-Run: No Code Duplication

The key insight: dry-run is not a special mode — it is the same pipeline with a no-op file system adapter injected.

Two implementations of `IFileSystem` (using the System.IO.Abstractions contract or a hand-rolled equivalent):

```
Production run:  inject FileSystem (wraps real System.IO)
Dry run:         inject DryRunFileSystem (all write operations are no-ops; reads are real)
Unit tests:      inject MockFileSystem (fully in-memory)
```

`DryRunFileSystem` inherits or delegates to `FileSystem` but overrides the mutating methods (`File.Move`, `File.Delete`, `File.WriteAllText`) with no-ops that return success. The orchestrator and conflict resolver never know the difference.

This is the approach confirmed by the `System.IO.Abstractions` maintainers and aligns with the pattern described in Code Maze's testing guide. (MEDIUM confidence — multiple independent sources agree.)

**Registration at startup (conceptual):**

```csharp
bool isDryRun = parseResult.GetValueForOption(dryRunOption);

services.AddSingleton<IFileSystem>(isDryRun
    ? new DryRunFileSystem()
    : new FileSystem());
```

---

## Testing Strategy for File System Operations

**Do not use temp directories in unit tests.** Use in-memory fakes.

| Test type | Mechanism | What it covers |
|-----------|-----------|---------------|
| Unit — Pattern Engine | Pure C# input/output, no IFileSystem needed | Wildcard compilation, regex correctness, transform ordering |
| Unit — Conflict Resolver | `MockFileSystem` pre-populated with existing files | Auto-numbering logic, edge cases (existing `file(1)` through `file(N)`) |
| Unit — Rename Orchestrator | `MockFileSystem` with a known directory tree | Full rename pipeline, dry-run toggle, skipped files, failure capture |
| Unit — Reporter | Captured `TestConsole` (IConsole mock) + `MockFileSystem` | Log file creation, summary counts, no-log-when-no-failures |
| Integration | Real temp folder, cleaned up in test teardown | End-to-end CLI invocation via `CommandLineBuilder` test host |

`System.IO.Abstractions.TestingHelpers.MockFileSystem` is the recommended tool (NuGet: `System.IO.Abstractions.TestingHelpers`, current version 22.x). It provides a pre-populated in-memory file system that satisfies the `IFileSystem` interface, requiring no disk access and no cleanup.

For the integration layer, use `System.CommandLine`'s built-in test console (`TestConsole`) and invoke the `RootCommand` directly without spawning a subprocess.

---

## Suggested Build Order (Phase Dependencies)

The dependency graph drives build order: lower layers must exist before upper layers can be wired.

```
Phase 1: IFileSystem abstraction + MockFileSystem wiring
         (unblocks all other components to be tested in isolation)

Phase 2: Pattern Engine — WildcardCompiler + WildcardPatternMatcher
         (pure logic, no dependencies, validates core value proposition early)

Phase 3: File Scanner + Conflict Resolver
         (both depend on IFileSystem, can be built in parallel)

Phase 4: Rename Orchestrator
         (integrates Phase 2 and 3; drives the full pipeline)

Phase 5: CLI Layer — System.CommandLine command/option/handler definitions
         (thin wiring; all logic lives below it)

Phase 6: Reporter
         (reads results produced by Orchestrator; can be stubbed until Phase 4 is done)

Phase 7: RegexPatternMatcher + --advanced flag wiring
         (mechanical addition; Pattern Engine interface is already established)

Phase 8: --recursive flag, log file output, integration tests
         (additive features on a working pipeline)
```

**Critical path:** Phase 1 → Phase 2 → Phase 4 → Phase 5. Everything else is parallel or additive.

---

## Scalability Considerations

This tool is bounded by file system I/O, not CPU or memory. For the target use case (CSV export directories, power users), these limits apply:

| Concern | At 100 files | At 10K files | Notes |
|---------|--------------|--------------|-------|
| Memory | Negligible | ~5 MB | Stream results; do not buffer all RenameResult objects |
| Performance | Instant | ~2–5s | Bottleneck is File.Move, not pattern matching |
| Conflict detection | O(1) per file | O(1) per file | Resolver checks IFileSystem.File.Exists inline |
| Log file | Not created | Written once at end | Accumulate failures in-memory, write once |

No concurrency is needed. Sequential processing simplifies conflict detection and avoids race conditions on the same directory. Async file I/O is not warranted for this scale.

---

## Key Architectural Decisions

| Decision | Rationale |
|----------|-----------|
| `IFileSystem` as the seam for dry-run | Single injection point eliminates all conditional branching in business logic |
| `IPatternMatcher` interface over a `bool isAdvanced` parameter | Callers remain ignorant of syntax mode; adding a third mode later requires no orchestrator changes |
| Fixed operation order baked into orchestrator (not configurable at runtime) | Matches PROJECT.md requirement; removes a whole class of ordering bugs |
| System.CommandLine 2.0.8 (stable) | Ships with .NET CLI itself; trim-friendly, AOT-capable, stable API; no alpha risk |
| No MediatR, no Generic Host | Tool has one command with no need for request/response routing; BCL DI via `ServiceCollection` is sufficient |
| Results as `IEnumerable<RenameResult>` (pull) | Reporter can stream output without buffering; orchestrator does not need to know about output format |

---

## Sources

- System.CommandLine NuGet (stable 2.0.8): https://www.nuget.org/packages/System.CommandLine/
- System.CommandLine official overview: https://learn.microsoft.com/en-us/dotnet/standard/commandline/ (MEDIUM confidence — official docs, updated 2025-12)
- System.IO.Abstractions GitHub: https://github.com/TestableIO/System.IO.Abstractions (HIGH confidence — official repo)
- System.IO.Abstractions.TestingHelpers NuGet 22.1.0: https://www.nuget.org/packages/System.IO.Abstractions.TestingHelpers/ (HIGH confidence — official package page)
- How to Mock the File System for Unit Testing in .NET — Code Maze: https://code-maze.com/dotnet-unit-testing-mock-file-system/ (MEDIUM confidence)
- Microsoft.Extensions.FileSystemGlobbing docs: https://learn.microsoft.com/en-us/dotnet/core/extensions/file-globbing (HIGH confidence — official docs, updated 2026-03)
- Clean CLI architecture with System.CommandLine: https://nikiforovall.blog/dotnet/cli/2021/06/06/clean-cli.html (LOW confidence — community blog, 2021)
