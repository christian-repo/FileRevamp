---
phase: 01
slug: core-rename-pipeline
status: verified
threats_open: 0
asvs_level: 1
created: 2026-06-01
---

# Phase 01 — Security

> Per-phase security contract: threat register, accepted risks, and audit trail.

---

## Trust Boundaries

| Boundary | Description | Data Crossing |
|----------|-------------|---------------|
| CLI args → RenameSettings | User-supplied directory paths and patterns arrive untrusted; Spectre.Console.Cli parses them before Execute sees them | directory paths, wildcard patterns, replace operands |
| RenameOrchestrator → IFileSystem | Computed destination paths passed to MoveFile may be crafted to traverse outside the target directory | file paths (computed from user patterns) |
| Pattern → Regex compilation | User wildcard pattern becomes a compiled Regex; catastrophic backtracking possible with adversarial inputs | wildcard string |
| --replace operand string | "old->new" format parsed at command entry; split on first "->" only | find/replace literal strings |
| RenameResult → Reporter → AnsiConsole | Filenames containing Spectre markup chars ([ ]) could inject markup if not escaped | filenames from disk |

---

## Threat Register

| Threat ID | Category | Component | Disposition | Mitigation | Status |
|-----------|----------|-----------|-------------|------------|--------|
| T-01-01 | Tampering | RenameOrchestrator — destPath construction | mitigate | `RenameOrchestrator.cs`: `Path.GetFullPath(destPath)` → `Path.GetDirectoryName` compared to `normalizedDir` (OrdinalIgnoreCase); emits `FailedResult("Path traversal rejected")` on mismatch | closed |
| T-01-02 | Tampering | RenameSettings.Path argument | mitigate | `RenameCommand.cs` line 49: `Path.GetFullPath(directoryPath)` at command entry before any downstream call | closed |
| T-01-03 | Denial of Service | WildcardCompiler — adversarial regex patterns | accept | Developer tool, no network surface; ReDoS risk low | closed |
| T-01-04 | Information Disclosure | Per-file console output lines | accept | Tool designed to output filenames; no PII; developer tool, no multi-user surface | closed |
| T-01-05 | Elevation of Privilege | IFileSystem.MoveFile targeting system files | mitigate | `FileSystem.cs`: `Directory.GetFiles(..., SearchOption.TopDirectoryOnly)` — non-recursive; no mechanism to reach outside user-supplied directory | closed |
| T-01-SC | Tampering | NuGet package supply chain | mitigate | `FileRevamp.csproj`: exactly two packages — `Spectre.Console.Cli 0.55.0` and `Microsoft.Extensions.FileSystemGlobbing 9.*`; both verified sources | closed |
| T-02-01 | Tampering | ReplaceTransform — invalid chars in computed filename | mitigate | `RenameOrchestrator.cs`: `Path.GetInvalidFileNameChars()` checked after all transforms; emits `FailedResult` with offending character | closed |
| T-02-02 | Tampering | RenameOrchestrator destPath (Plan 02 extension) | mitigate | Same guard as T-01-01 — inherited; no separate code path for Plan 02 | closed |
| T-02-03 | Denial of Service | Unanchored regex — catastrophic backtracking on adversarial filenames | accept | Developer tool; pattern inputs are author-controlled; no network surface | closed |
| T-02-04 | Information Disclosure | FileDiscovery enumerates all files in user-specified directory | accept | User specified the directory; enumeration is the expected behavior | closed |
| T-02-SC | Tampering | Supply chain — Plan 02 | mitigate | No new packages added in Plan 02; same verified packages as Plan 01 | closed |
| T-03-01 | Tampering | Reporter → AnsiConsole markup injection via filenames | mitigate | `RenameCommand.cs`: `Markup.Escape(...)` wraps all three `MarkupLine` call sites (per-file, dry-run summary, live summary) | closed |
| T-03-02 | Information Disclosure | Summary line reveals failure count | accept | Failures are about the user's own files; intentional output required by RPRT-02 | closed |
| T-03-03 | Denial of Service | Large batch materializes all results in memory | mitigate | `RenameCommand.cs`: `.ToList()` acknowledged as bounded risk for Phase 1 flat directory; `TODO(Phase 2): stream results for large batches` | closed |
| T-03-SC | Tampering | Spectre.Console.Cli.Testing supply chain | mitigate | `FileRevamp.Tests.csproj`: `Spectre.Console.Cli.Testing 0.55.0` — same vendor and version as production dep; verified | closed |

*Status: open · closed*
*Disposition: mitigate (implementation required) · accept (documented risk) · transfer (third-party)*

---

## Accepted Risks Log

| Risk ID | Threat Ref | Rationale | Accepted By | Date |
|---------|------------|-----------|-------------|------|
| AR-01 | T-01-03 | WildcardCompiler patterns are developer-authored, not end-user HTTP inputs; tool has no network surface; ReDoS risk is low | plan-time design decision | 2026-05-31 |
| AR-02 | T-01-04 | Filenames are expected output by design; developer tool with no multi-user surface | plan-time design decision | 2026-05-31 |
| AR-03 | T-02-03 | Unanchored regex patterns come from the same developer-controlled input as the anchored patterns; no escalation path | plan-time design decision | 2026-05-31 |
| AR-04 | T-02-04 | User explicitly provided the directory; enumeration is the core feature | plan-time design decision | 2026-05-31 |
| AR-05 | T-03-02 | Summary counts are required output per RPRT-02; user's own files | plan-time design decision | 2026-05-31 |

---

## Implementation Notes

**Structural note (informational, ASVS L1 acceptable):** `Reporter.cs` itself does not call `Markup.Escape()` — the caller contract is documented at line 16. `RenameCommand` correctly applies `Markup.Escape()` at all three call sites. The mitigation is real but relies on caller discipline rather than enforcement at the formatter level. Recommended improvement for Phase 3: move escape into `Reporter.FormatResultLine` directly.

---

## Security Audit Trail

| Audit Date | Threats Total | Closed | Open | Run By |
|------------|---------------|--------|------|--------|
| 2026-06-01 | 15 | 15 | 0 | Claude (gsd-security-auditor) — ASVS Level 1 |

---

## Sign-Off

- [x] All threats have a disposition (mitigate / accept / transfer)
- [x] Accepted risks documented in Accepted Risks Log
- [x] `threats_open: 0` confirmed
- [x] `status: verified` set in frontmatter

**Approval:** verified 2026-06-01
