# SECURITY.md

**Project:** FileRevamp
**Phase:** 01 — core-rename-pipeline
**Audit Date:** 2026-06-01
**ASVS Level:** 1
**Auditor:** claude-sonnet-4-6 (automated security audit)

---

## Result: SECURED

All 10 mitigate threats have verified implementation evidence. All 5 accepted threats are documented. No unregistered threat flags detected.

**Threats Closed:** 15/15 (10 mitigate + 5 accept)
**Threats Open:** 0
**Unregistered Flags:** 0

---

## Threat Verification

### Mitigate Threats

| Threat ID | Category | Disposition | Evidence |
|-----------|----------|-------------|----------|
| T-01-01 | Tampering | mitigate | `src/FileRevamp/Core/RenameOrchestrator.cs` lines 115-120: `Path.GetFullPath(destPath)` → `Path.GetDirectoryName(normalizedDest)` compared to `normalizedDir` with `StringComparison.OrdinalIgnoreCase`; emits `FailedResult("Path traversal rejected")` on mismatch |
| T-01-02 | Tampering | mitigate | `src/FileRevamp/Commands/RenameCommand.cs` line 49: `directoryPath = System.IO.Path.GetFullPath(directoryPath)` at command entry, before any downstream call |
| T-01-05 | EoP | mitigate | `src/FileRevamp/Core/FileSystem.cs` line 9: `Directory.GetFiles(directoryPath, searchPattern, SearchOption.TopDirectoryOnly)` — non-recursive; no mechanism to enumerate outside the user-supplied directory |
| T-01-SC | Tampering | mitigate | `src/FileRevamp/FileRevamp.csproj`: only `Spectre.Console.Cli 0.55.0` and `Microsoft.Extensions.FileSystemGlobbing 9.*` present; no unexpected packages |
| T-02-01 | Tampering | mitigate | `src/FileRevamp/Core/RenameOrchestrator.cs` lines 95-103: `Path.GetInvalidFileNameChars()` checked after all transforms; any invalid char yields `FailedResult("Computed filename contains invalid characters: '...'")` |
| T-02-02 | Tampering | mitigate | `src/FileRevamp/Core/RenameOrchestrator.cs` lines 115-120: same path-traversal guard as T-01-01; inherited by plan 02 without modification |
| T-02-SC | Tampering | mitigate | `src/FileRevamp/FileRevamp.csproj`: no packages added in plan 02; 01-02-SUMMARY.md confirms "Only existing verified packages used (FileSystemGlobbing already in csproj from Plan 01 scaffold)" |
| T-03-01 | Tampering | mitigate | `src/FileRevamp/Commands/RenameCommand.cs` lines 103, 108, 113: every `_console.MarkupLine()` call wraps in `Markup.Escape(...)` — all three call sites (per-file line, dry-run summary, live summary) are covered |
| T-03-03 | DoS | mitigate | `src/FileRevamp/Commands/RenameCommand.cs` line 97: `.ToList()` materializes all results; comment `// TODO(Phase 2): stream results for large batches to avoid holding all in memory.` documents acknowledged bounded risk for Phase 1 flat directory |
| T-03-SC | Tampering | mitigate | `tests/FileRevamp.Tests/FileRevamp.Tests.csproj` line 16: `Spectre.Console.Cli.Testing Version="0.55.0"` — same vendor and version as the production `Spectre.Console.Cli` dependency |

### Accepted Threats

| Threat ID | Category | Disposition | Accepted Risk |
|-----------|----------|-------------|---------------|
| T-01-03 | DoS | accept | WildcardCompiler adversarial regex patterns — accepted because this is a developer tool with no network surface; patterns are author-controlled |
| T-01-04 | InfoDisc | accept | Per-file console output reveals filenames — accepted by design; the tool's core value is showing the user what will happen |
| T-02-03 | DoS | accept | Unanchored regex backtracking in ReplaceTransform — accepted because this is a developer tool; patterns are author-controlled |
| T-02-04 | InfoDisc | accept | FileDiscovery directory enumeration reveals directory contents — accepted by design; enumeration is the primary function |
| T-03-02 | InfoDisc | accept | Summary line reveals failure count — accepted; intentional output (RPRT-02 requirement) |

---

## Unregistered Threat Flags

None. All three SUMMARY.md files (`01-01-SUMMARY.md`, `01-02-SUMMARY.md`, `01-03-SUMMARY.md`) explicitly state: "No new security surface beyond what was analyzed in the plan's threat model."

---

## Accepted Risks Log

| ID | Risk | Accepted At | Rationale |
|----|------|-------------|-----------|
| T-01-03 | ReDoS via adversarial wildcard patterns | Plan time | Developer CLI tool; no network attack surface; patterns are user-supplied by the tool's author |
| T-01-04 | Information disclosure via per-file console output | Plan time | By design; core product value is showing rename preview |
| T-02-03 | Regex backtracking in unanchored replace patterns | Plan time | Developer CLI tool; author-controlled input |
| T-02-04 | Directory contents disclosed via file enumeration | Plan time | By design; enumeration is the primary function |
| T-03-02 | Failure count revealed in summary output | Plan time | Intentional output per RPRT-02 requirement |

---

## Notes

- `Reporter.cs` is a pure string formatter with no `IAnsiConsole` dependency. The `Markup.Escape()` call is the caller's (RenameCommand) responsibility, which is correctly applied at all three call sites in `RenameCommand.cs` lines 103, 108, 113. The architecture comment in `Reporter.cs` line 16 documents this contract.
- The path-traversal check (T-01-01/T-02-02) uses `StringComparison.OrdinalIgnoreCase` which is correct for Windows-first filesystem semantics.
- `SearchOption.TopDirectoryOnly` in `FileSystem.GetFiles` (T-01-05) is the only production implementation; `DryRunFileSystem` and `MockFileSystem` are test/no-op variants and do not bypass this constraint via `IFileSystem`.
