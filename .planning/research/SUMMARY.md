# Project Research Summary

**Project:** FileRevamp
**Domain:** Batch file rename CLI tool (.NET C#, dotnet global tool)
**Researched:** 2026-05-31
**Confidence:** HIGH

## Executive Summary

FileRevamp is a focused, single-command CLI rename tool targeting developers and power users who process bulk file exports. The research confirms the tool should be built as a .NET 9 dotnet global tool using Spectre.Console.Cli for argument parsing and rich console output, with all business logic isolated in a pure domain core that has zero UI dependencies. The recommended architecture is a four-layer pipeline: CLI entry point to Pattern Engine to Rename Orchestrator to Reporter. The key structural insight is that IFileSystem is the single seam separating dry-run from live-run execution with no conditional branching in business logic, only a different adapter injected. This design makes the entire rename pipeline fully unit-testable without touching the disk.

The feature research confirms that the current spec already covers all table stakes: dry-run preview, before/after listing, conflict detection with auto-numbering, error logging, glob file selection, recursive traversal, regex mode, and path-safety validation. The custom wildcard syntax using braced tokens is a genuine differentiator -- no surveyed tool (rnr, F2, BRU, Advanced Renamer, rename-cli) offers a simplified non-regex pattern syntax for filename transforms. Phase 2 candidates include sequential padding, case transforms, and extension normalization, all low-complexity additions.

The top risks are concentrated in Phase 1. Case-only renames are a silent no-op on NTFS and require a two-step rename through a temp name. The wildcard-to-regex conversion has a strict ordering constraint: escape metacharacters first, substitute tokens second, then add anchors. Conflict resolution must use a live HashSet of occupied names that accumulates as the batch runs. All four Phase 1 risks are well-understood with clear documented prevention strategies.
---

## Key Findings

### Recommended Stack

The tool requires only three NuGet dependencies beyond the BCL: Spectre.Console.Cli 0.55.0 (covers both argument parsing and rich console output), Microsoft.Extensions.FileSystemGlobbing 9.x (in-box file selection), and System.IO.Abstractions.TestingHelpers 22.x (test-only in-memory file system). The test stack is xUnit 2.9.x plus FluentAssertions 7.x plus Spectre.Console.Cli.Testing 0.55.0. No logging framework, no MediatR, no Generic Host, and no additional NuGet packages are warranted.

The csproj targets net9.0 (STS preferred over LTS for a power-user tool where startup performance matters), enables PackAsTool, and sets RollForward=Major so the installed tool works on future .NET versions without reinstall. All pattern matching uses BCL System.Text.RegularExpressions.Regex with a hand-rolled wildcard translator of approximately 30 lines.

**Core technologies:**
- **.NET 9 / C# 13**: Runtime -- STS performance wins outweigh LTS concerns for a CLI tool installed by power users
- **Spectre.Console.Cli 0.55.0**: CLI parsing and rich output -- single dependency replaces System.CommandLine plus a separate output library
- **Microsoft.Extensions.FileSystemGlobbing 9.x**: File selection -- official BCL-adjacent library; supports double-star recursive globs that Directory.GetFiles cannot express
- **BCL Regex plus custom wildcard translator**: Filename transform matching -- zero external dependency; GeneratedRegex attribute eliminates allocations at runtime
- **System.IO.Abstractions.TestingHelpers 22.x** (test-only): File system seam -- enables fully in-memory unit tests with no temp directory management
- **xUnit 2.9.x + FluentAssertions 7.x + Spectre.Testing 0.55.0**: Test stack -- standard .NET community practice; Spectre.Testing enables in-process CLI command testing
### Expected Features

**Must have (table stakes) -- ship in v1:**
- Dry-run with explicit zero-files-modified confirmation -- users will not trust a batch tool without preview
- Before/after per-file listing on every processed file
- Summary count at end of run (succeeded / failed / skipped)
- Pre-flight conflict detection before any file is touched -- mid-run detection causes partial batches
- Conflict resolution via auto-numbering file(1).csv -- Windows convention, safest default
- Error log in target directory on any failure -- essential for auditing bulk CSV workflows
- Glob target selection with --recursive flag
- Custom wildcard syntax for non-regex users -- the project primary differentiator
- --advanced flag for full regex access
- Fixed operation order: removes first, then replacements
- Path-safety validation: forbidden chars, reserved names (CON, NUL, AUX, etc.), MAX_PATH, trailing dots/spaces
- --help with concrete pattern examples

**Should have (v1 or near-term):**
- Parse-time detection of bare asterisk in wildcard mode with targeted error message
- No-anchor warning in --advanced mode
- Per-file inline output as the rename runs (not just final summary)

**Defer to v2+:**
- Sequential padding for auto-numbering (configurable width)
- Case transforms (--upper, --lower, --title)
- Extension normalization (--normalize-ext)

**Do not build:**
- Undo/rollback, directory renaming, metadata renaming (EXIF/ID3), GUI, TUI, config file presets, plugin architecture, cross-drive file moves
### Architecture Approach

The architecture follows a strict four-layer pipeline where no component reaches backward through the stack. The CLI layer is the sole assembly point; it constructs a RenameRequest value object and wires the DI container. IFileSystem is injected as FileSystem (live), DryRunFileSystem (all write ops are no-ops), or MockFileSystem (in-memory for tests). Results flow out as IEnumerable of RenameResult (pull), allowing the Reporter to stream output without buffering.

**Major components and build order:**
1. **IFileSystem abstraction plus MockFileSystem wiring** -- unblocks all other components for isolated testing; must exist first
2. **Pattern Engine (WildcardCompiler plus WildcardPatternMatcher)** -- pure logic, no file system dependency; validates the core value proposition earliest
3. **File Scanner plus Conflict Resolver** -- both depend on IFileSystem; can be built in parallel
4. **Rename Orchestrator** -- integrates Pattern Engine, File Scanner, and Conflict Resolver; drives the full pipeline
5. **CLI Layer** -- thin Spectre.Console.Cli command/settings wiring; all logic lives below it
6. **Reporter** -- reads results produced by Orchestrator; can be stubbed until Orchestrator is complete
7. **RegexPatternMatcher plus --advanced flag wiring** -- additive; Pattern Engine interface already established
8. **--recursive support, log file output, integration tests** -- additive features on a working pipeline

### Critical Pitfalls

1. **Case-only rename is a silent no-op on NTFS** -- File.Move returns success but does nothing when source and destination differ only in casing. Prevention: two-step rename through a guaranteed-unique temp name, then rename to final destination. Must be in the core rename engine before any replace operation is wired up.

2. **Wildcard-to-regex conversion ordering is strict** -- escape metacharacters first via Regex.Escape, then substitute brace tokens to their regex equivalents, then wrap in caret-dollar anchors. Reversing this causes patterns to silently over-match or fail entirely. Prevention: table-driven unit tests covering literal dots, parentheses in filenames, and patterns at all positions.

3. **Auto-numbering cascade and same-batch collisions overwrite files** -- two source files that compute to the same output name will collide if conflict checking only calls File.Exists at rename time. Prevention: live HashSet of occupied names (case-insensitive) built at batch-start, updated as each output name is committed or planned.

4. **Reserved Windows filenames not caught by Path.GetInvalidFileNameChars** -- a transform producing NUL.csv throws IOException mid-batch leaving partial state. Prevention: pre-flight validation pass against the full reserved-name list (CON, PRN, AUX, NUL, COM0-COM9, LPT0-LPT9).

5. **Directory.EnumerateFiles aborts entire traversal on first access-denied subdirectory** -- SearchOption.AllDirectories throws UnauthorizedAccessException at the first restricted folder and cancels everything. Prevention: manual recursive enumeration with per-subdirectory try/catch; log inaccessible directories as warnings and continue.
---

## Implications for Roadmap

Based on combined research, the dependency graph and pitfall concentration point to a three-phase structure with an optional fourth phase for differentiating features.

### Phase 1: Core Rename Pipeline (Foundation)

**Rationale:** Four of the twelve documented pitfalls are concentrated in pipeline core logic. They must be correct and tested before the CLI layer is built on top of them. IFileSystem must exist before anything else can be unit tested.

**Delivers:** A fully testable rename pipeline with no UI dependency. Running RenameOrchestrator in unit tests against a MockFileSystem produces correct RenameResult arrays for all transform and conflict scenarios.

**Addresses from FEATURES.md:** Custom wildcard syntax, remove plus replace transforms, pre-flight conflict detection, auto-number resolution, path-safety validation, error log

**Avoids from PITFALLS.md:** Pitfall 1 (case-only no-op), Pitfall 2 (auto-number cascade), Pitfall 3 (reserved filenames), Pitfall 4 (wildcard conversion ordering), Pitfall 6 (trailing dots/spaces), Pitfall 7 (read-only log directory), Pitfall 10 (Unicode NFD/NFC), Pitfall 12 (log file self-rename)

**Research flag:** Standard patterns -- no additional research needed.

---

### Phase 2: CLI Integration and User-Facing Output

**Rationale:** The CLI layer is deliberately thin. It should be built after the core pipeline is proven correct in unit tests. Reporter and Spectre.Console output formatting belong here because they have no bearing on correctness.

**Delivers:** A fully functional filerevamp command invocable from the shell, with dry-run preview, per-file progress lines, summary count, and error log output. Passes all table-stakes feature checks.

**Addresses from FEATURES.md:** Dry-run confirmation, before/after per-file listing, summary count, glob target selection, --help with examples, --advanced regex mode, bare-asterisk detection error message, no-anchor warning

**Uses from STACK.md:** Spectre.Console.Cli 0.55.0, Spectre.Console.Cli.Testing, Microsoft.Extensions.FileSystemGlobbing

**Avoids from PITFALLS.md:** Pitfall 9 (unanchored regex surprises), Pitfall 11 (glob vs wildcard user confusion)

**Research flag:** Standard patterns -- Spectre.Console.Cli Command/Settings wiring is well-documented.

---

### Phase 3: Recursive Support and Robustness

**Rationale:** --recursive introduces two distinct new pitfall categories (MAX_PATH and EnumerateFiles abort on access-denied subdirectories) that are best isolated from Phase 2 CLI work.

**Delivers:** --recursive flag fully operational with per-subdirectory error isolation, graceful handling of access-denied directories, and long-path support via application manifest.

**Addresses from FEATURES.md:** Recursive directory traversal, nested export directory processing

**Avoids from PITFALLS.md:** Pitfall 5 (MAX_PATH over 260, longPathAware manifest), Pitfall 8 (EnumerateFiles aborts on access-denied, manual recursive enumeration)

**Research flag:** Standard patterns -- MAX_PATH manifest opt-in and manual recursive enumeration documented in official Microsoft Learn docs.

---

### Phase 4: Differentiating Features (v2 candidates)

**Rationale:** Additive features with no architectural dependencies on each other or on earlier phases. Deferring ensures v1 ships as a trustworthy, focused tool.

**Delivers:** Sequential padding for auto-numbered files, case transforms (--upper, --lower, --title), extension normalization (--normalize-ext)

**Research flag:** No research needed -- low-complexity additions to the existing transform pipeline.