# FileRevamp

## What This Is

FileRevamp is a .NET C# console application for batch-renaming files using pattern-based transforms. Users pass remove/replace operations on the command line — with either a simplified wildcard syntax or full regex — and the tool renames all matching files in a target directory, reporting results inline and logging failures to a file.

## Core Value

A file renaming operation must be predictable and reversible: show the user exactly what will happen before doing it, and never silently corrupt filenames.

## Requirements

### Validated

*Validated in Phase 1: Core Rename Pipeline*

- [x] User can rename files by passing a directory path or glob pattern as the target
- [x] User can specify remove operations using simplified wildcard syntax (e.g. `_{*}new_{*}`)
- [x] User can specify replace/transform operations (e.g. replace `.` with `-`)
- [x] Rename operations apply in fixed order: removes first, then replacements
- [x] User can preview all renames with `--dry-run` without touching any files
- [x] At end of run, a summary is displayed: count of successful renames and count of failures

*Validated in Phase 2: Safety and Reporting*

- [x] When renamed file already exists, tool auto-numbers the new name (e.g. `file(1).csv`) — two-pass Plan()+Execute() with CollisionResolver; pre-flight before any MoveFile call
- [x] On failure, a log file is created in the target folder showing filename and failure reason — lazy FailureLogger; never created if all renames succeed; excluded from rename batch
- [x] User can view usage and examples via `--help` flag — wildcard (`{*}`) and replace (`->`) examples via `AddExample`; `--version` via `SetApplicationVersion("1.0.0")`

### Active

- [ ] User can specify remove/match operations using full regex via `--advanced` flag
- [ ] User can recurse into subdirectories with `--recursive` flag

### Out of Scope

- GUI / interactive UI — command-line only
- Undo/rollback of renames — users should use `--dry-run` before committing
- Renaming directories — files only
- Cross-drive moves — rename within the same filesystem only

## Context

- Target users: developers and power users who work with bulk file exports (e.g. CSV dumps with system-generated names)
- Wildcard syntax mirrors regex quantifiers conceptually: `{*}` = any chars, `{+}` = one or more chars, `{?}` = zero or one char
- Full regex mode (`--advanced`) targets users comfortable with regex patterns
- Log file lives in the target directory alongside the files being renamed

## Constraints

- **Tech stack**: .NET C# console app — no external dependencies beyond the BCL preferred
- **Platform**: Windows-first (paths, casing); Linux/Mac compatibility is a bonus not a hard requirement
- **Output**: Human-readable console output; log file in plain text

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Fixed operation order (removes → replacements) | Prevents order-dependent bugs; simpler mental model for users | — Pending |
| Auto-number on conflict (file(1).csv) | Preserves originals; safer than overwrite | — Pending |
| Wildcard syntax with regex escape hatch | Lowers barrier for casual users while serving power users | — Pending |

## Evolution

This document evolves at phase transitions and milestone boundaries.

**After each phase transition** (via `/gsd-transition`):
1. Requirements invalidated? → Move to Out of Scope with reason
2. Requirements validated? → Move to Validated with phase reference
3. New requirements emerged? → Add to Active
4. Decisions to log? → Add to Key Decisions
5. "What This Is" still accurate? → Update if drifted

**After each milestone** (via `/gsd-complete-milestone`):
1. Full review of all sections
2. Core Value check — still the right priority?
3. Audit Out of Scope — reasons still valid?
4. Update Context with current state

---
*Last updated: 2026-06-02 after Phase 2 completion*
