# Walking Skeleton: FileRevamp

**Phase:** 1 — Core Rename Pipeline
**Created:** 2026-05-31
**Purpose:** Record the architectural decisions that Phase 1 locks in and all subsequent phases build on without renegotiating.

---

## What the Skeleton Delivers

The thinnest possible end-to-end stack where:

1. `dotnet run --project src/FileRevamp -- --help` works and shows usage
2. `dotnet run --project src/FileRevamp -- ./some-dir --remove "_{*}new_{*}" --dry-run` runs the pipeline and shows `[DRY RUN]` output
3. One in-process test proves `WildcardCompiler.ToRegex` + `RenameOrchestrator` produce correct before/after results against a `MockFileSystem` with no disk access

---

## Architectural Decisions (Locked for v1)

| Concern | Decision | Rationale |
|---------|----------|-----------|
| CLI framework | Spectre.Console.Cli 0.55.0 | Single NuGet replaces System.CommandLine + separate output lib; integrates ANSI/table/markup natively |
| File discovery | Microsoft.Extensions.FileSystemGlobbing 9.x | Official BCL-adjacent; supports `**` double-star globs Directory.GetFiles cannot express |
| Filename transform matching | BCL `System.Text.RegularExpressions.Regex` + custom `WildcardCompiler` (~30 lines) | Zero external dependency; GeneratedRegex eliminates allocations |
| File system seam | Hand-rolled `IFileSystem` interface + `FileSystem` (real) + `DryRunFileSystem` (no-op writes) + `MockFileSystem` (in-memory) | Single injection point eliminates all conditional branching in business logic; no need for `System.IO.Abstractions` NuGet |
| Dry-run mechanism | `DryRunFileSystem` injected instead of `FileSystem`; orchestrator unchanged | Removes all `if (isDryRun)` branching from core logic |
| Target framework | `net9.0` (STS) | Startup performance wins matter for a CLI tool; power-user audience has no enterprise LTS requirement |
| Project structure | Single csproj (`src/FileRevamp/`) + test project (`tests/FileRevamp.Tests/`) | Small focused CLI tool; namespace separation sufficient; no multi-project overhead |
| Operation order | Removes applied first (in CLI arg order), replacements applied second (in CLI arg order) — fixed, not configurable | Matches core value statement; eliminates ordering bugs |
| Conflict resolution | NOT in Phase 1 scope — deferred to Phase 2 (`SAFE-01`, `SAFE-02`) | Phase 1 delivers the pipeline; Phase 2 hardens it |
| Output format | Per-file lines: `[DRY RUN] old.csv → new.csv` or `old.csv → new.csv` + end summary | Matches all surveyed rename tools; users scan for 0 failures |
| Logging framework | None — BCL `File.AppendAllText` for error log | One-liner BCL; Serilog/NLog is over-engineering for a text file |

---

## Directory Layout

```
FileRevamp/                          ← repo root
├── FileRevamp.sln
├── src/
│   └── FileRevamp/
│       ├── FileRevamp.csproj        ← net9.0, PackAsTool, Spectre.Console.Cli 0.55.0
│       ├── Program.cs               ← entry point: build CommandApp, register RenameCommand
│       ├── Commands/
│       │   ├── RenameCommand.cs     ← Command<RenameSettings>
│       │   └── RenameSettings.cs   ← CommandSettings: directory/glob, --remove, --replace, --dry-run
│       ├── Core/
│       │   ├── IFileSystem.cs       ← hand-rolled interface (File.Move, File.Exists, Directory.GetFiles)
│       │   ├── FileSystem.cs        ← real System.IO implementation
│       │   ├── DryRunFileSystem.cs  ← delegates to FileSystem for reads; no-ops writes
│       │   ├── WildcardCompiler.cs  ← static: ToRegex(pattern) with strict escape→substitute→anchor order
│       │   ├── WildcardPatternMatcher.cs ← applies compiled remove patterns to a filename string
│       │   ├── ReplaceTransform.cs  ← applies a single find→replace substitution to a string
│       │   ├── FileDiscovery.cs     ← wraps FileSystemGlobbing Matcher; returns file list from dir + glob
│       │   └── RenameOrchestrator.cs ← drives per-file pipeline: scan→remove→replace→dry/live
│       └── Output/
│           ├── RenameResult.cs      ← value object: OriginalName, NewName, Status (Renamed/DryRun/Skipped/Failed)
│           └── Reporter.cs          ← formats per-file lines + summary to IAnsiConsole
└── tests/
    └── FileRevamp.Tests/
        ├── FileRevamp.Tests.csproj  ← xUnit 2.9.x, FluentAssertions 7.x, Spectre.Testing 0.55.0
        ├── Core/
        │   ├── WildcardCompilerTests.cs
        │   ├── WildcardPatternMatcherTests.cs
        │   ├── ReplaceTransformTests.cs
        │   └── RenameOrchestratorTests.cs
        └── Commands/
            └── RenameCommandTests.cs
```

---

## Tech Stack

| Layer | Technology | Version |
|-------|-----------|---------|
| Runtime | .NET | 9.0 (net9.0 TFM) |
| Language | C# | 13 |
| CLI parsing + output | Spectre.Console.Cli | 0.55.0 |
| File discovery | Microsoft.Extensions.FileSystemGlobbing | 9.* |
| Test runner | xUnit | 2.9.x |
| Assertions | FluentAssertions | 7.x |
| CLI test harness | Spectre.Console.Cli.Testing | 0.55.0 |
| File system seam | Hand-rolled IFileSystem | (no NuGet) |

---

## Critical Invariants (Must Hold in Every Phase)

1. **Wildcard-to-regex conversion order:** `Regex.Escape(raw)` → substitute `\{\*\}` → `(.*)`, `\{\+\}` → `(.+)`, `\{\?\}` → `(.?)` → wrap in `^...$`. Reversing any step causes silent over-match or under-match.
2. **Operation order is fixed:** all `--remove` patterns applied first (CLI arg order), all `--replace` transforms applied second (CLI arg order). Never interleaved.
3. **IFileSystem is the single dry-run seam.** No `if (isDryRun)` branches inside `WildcardPatternMatcher`, `ReplaceTransform`, or `RenameOrchestrator`. Only the injected `IFileSystem` variant differs.
4. **Enumerate files before opening log.** Never let the log file appear in the candidate list.
5. **Phase 2 adds conflict resolution.** Phase 1 skips any file whose computed name already exists in the directory (emits `RenameResult.Skipped` with reason). Phase 2 will replace the skip with auto-numbering.
