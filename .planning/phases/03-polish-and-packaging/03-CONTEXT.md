# Phase 3: Polish and Packaging - Context

**Gathered:** 2026-06-03
**Status:** Ready for planning

<domain>
## Phase Boundary

Package FileRevamp as a dotnet global tool, write integration tests that verify the complete rename pipeline end-to-end (including documented edge cases), and confirm the tool handles all specified scenarios correctly. No new user-facing features are added — this is a delivery and quality phase.

</domain>

<decisions>
## Implementation Decisions

### Edge-Case Integration Tests
- **D-01:** Add integration tests at the CLI level (CommandAppTester through the full command) — not unit-only. This level catches wiring gaps that unit tests miss (as seen in Phase 2's code review, where FailureLogger was built but not wired).
- **D-02:** Test scope covers all of the following scenarios:
  1. Filenames with literal dots and parentheses in wildcard patterns (e.g. `report.new.(2024).csv` with pattern `.(2024)`) — expect the rename to work correctly without regex errors
  2. Output names that collide within the same batch — dry-run shows auto-numbered resolved names; live run renames all files (no skips)
  3. Log file exclusion — `rename-failures.log` already present in directory is never processed as a rename candidate
  4. Empty directory — tool exits 0 with zero-result summary, no crash
  5. Unicode filenames (accented characters, e.g. `café_new.csv`) — rename succeeds correctly
  6. Long filenames (near Windows MAX_PATH, ~255 chars) — process without error
- **D-03:** For unicode and long filenames: test for **success** — the rename should complete correctly, not fail gracefully. These are valid real-world inputs.

### Package Metadata and Install Verification
- **Left to research/planning:** Researcher should determine the correct `PackageId`, `Version`, `Authors`, `Description`, license field, and whether a `<RepositoryUrl>` is needed. The `PackAsTool=true` and `ToolCommandName=filerevamp` scaffold is already in .csproj.
- **Install verification:** Phase 3 must verify that `dotnet pack` produces a valid .nupkg and `dotnet tool install --global` from the local nupkg succeeds, and `filerevamp --version` runs from any prompt.

### Integration Test Architecture
- **Left to research/planning:** Currently `RenameCommand` hardcodes `new FileSystem()` or `new DryRunFileSystem()`. Tests use CommandAppTester + temp directories. The ROADMAP goal is "no temp directory management." Researcher should determine whether to inject IFileSystem into the command constructor, or use temp directories with improved cleanup, or write integration tests at the orchestrator level. Either approach is acceptable as long as the ROADMAP success criteria are met.

### Claude's Discretion
- Specific test method names, file structure for integration tests, and exact assertion wording
- Whether to add a `Makefile` or `just` recipe for `pack` and `install` convenience targets
- How to handle MAX_PATH in tests (whether to use actual 255-char names or simulate near-MAX_PATH scenarios)

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase Goals and Success Criteria
- `.planning/ROADMAP.md` §"Phase 3: Polish and Packaging" — the 3 success criteria that define done
- `.planning/REQUIREMENTS.md` — all v1 requirements are already checked; verify nothing was missed

### Existing Implementation (understand before extending)
- `src/FileRevamp/Core/WildcardCompiler.cs` — Regex.Escape runs BEFORE brace-token substitution; this is the mechanism that makes literal dots/parens safe. CRITICAL: do not change the conversion order.
- `src/FileRevamp/Core/CollisionResolver.cs` — auto-numbering logic; has MaxAttempts=9999 guard added in Phase 2
- `src/FileRevamp/Commands/RenameCommand.cs` — log-file exclusion filter and FailureLogger wiring are here
- `src/FileRevamp/Program.cs` — SetApplicationVersion("1.0.0") and AddExample calls
- `src/FileRevamp/FileRevamp.csproj` — PackAsTool=true, ToolCommandName=filerevamp, PackageOutputPath=./nupkg already set

### Existing Test Infrastructure
- `tests/FileRevamp.Tests/Commands/RenameCommandTests.cs` — CommandAppTester pattern, CreateTempDir() helper, IDisposable cleanup — extend or replace this pattern for Phase 3 integration tests
- `tests/FileRevamp.Tests/Core/WildcardCompilerTests.cs` — existing unit coverage of WildcardCompiler; review before adding edge-case tests
- `tests/FileRevamp.Tests/Core/CollisionResolverTests.cs` — existing collision unit tests

### Prior Phase Context
- `.planning/phases/02-safety-and-reporting/02-REVIEW.md` — code review findings from Phase 2; note CR-04 (CancellationToken false positive confirmed working) and WR-04 (MockFileSystem in production assembly — may be worth moving in Phase 3)

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `MockFileSystem` (currently in `src/FileRevamp/Core/`) — in-memory IFileSystem with MoveCallCount tracking. If Phase 3 moves it to the test project (per WR-04 from Phase 2 review), it can be used to inject a pre-seeded filesystem into RenameCommand for in-memory integration tests.
- `CommandAppTester` + `FakeTypeRegistrar` pattern in `RenameCommandTests.cs` — already wires IAnsiConsole injection; the same DI pattern can inject IFileSystem if the command constructor accepts it.
- `CreateTempDir()` helper — reusable for any test that genuinely needs disk I/O (e.g., the `dotnet pack` / install verification test)

### Established Patterns
- `WildcardCompiler.ToRegex` — strict 4-step conversion order (escape → substitute → anchor → compile). The Regex.Escape step is what makes literal `.`, `(`, `)` in patterns safe.
- `RenameCommand.Execute` uses `CancellationToken` as optional third parameter (Spectre.Console 0.55 supports this — confirmed working in Phase 2).
- Tests use FluentAssertions `.Should().Contain()` / `.Should().Be()` / `.Should().BeTrue()` throughout.

### Integration Points
- Any IFileSystem injection into `RenameCommand` must be backward-compatible: null default → production FileSystem (same pattern as the IAnsiConsole injection already in the constructor)
- The `dotnet pack` / install verification belongs in a separate test class or shell script — it invokes a subprocess and doesn't fit the in-process CommandAppTester pattern

</code_context>

<specifics>
## Specific Ideas

- "All of them" when asked about edge-case scope — user wants comprehensive coverage including unicode and long filenames, not just the 3 ROADMAP baseline cases.
- Integration tests should go through the full CLI (CommandAppTester) — not just orchestrator-level — because that's where wiring bugs appear.
- Unicode test expectation: rename succeeds (not "graceful failure") — these are valid real-world inputs on Windows and .NET 9.

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 3-Polish and Packaging*
*Context gathered: 2026-06-03*
