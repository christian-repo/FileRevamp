# Roadmap: FileRevamp

## Overview

FileRevamp ships in three phases. Phase 1 builds the rename pipeline end-to-end so the tool does what it promises on first invocation: target files, apply wildcard and replacement transforms in fixed order, preview with dry-run, and report per-file results with a summary. Phase 2 hardens the tool so it can never corrupt a batch: pre-flight conflict detection, auto-numbering for collisions, error log output, and a -help command that shows users exactly how to invoke it. Phase 3 packages the tool as a dotnet global tool and validates the complete surface with integration tests, making it ready to install and distribute.

## Phases

**Phase Numbering:**

- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [x] **Phase 1: Core Rename Pipeline** - Targeting, transforms, dry-run, per-file output, and summary working end-to-end *(completed 2026-05-31)*
- [ ] **Phase 2: Safety and Reporting** - Pre-flight conflict detection, auto-numbering, error log file, and -help command
- [ ] **Phase 3: Polish and Packaging** - dotnet global tool packaging, integration tests, and edge-case hardening

## Phase Details

### Phase 1: Core Rename Pipeline

**Goal**: Users can invoke the tool against a real directory, apply remove and replace transforms using wildcard syntax, preview with --dry-run, and see per-file results plus a summary count — with no file touched during dry-run
**Mode:** mvp
**Depends on**: Nothing (first phase)
**Requirements**: TARG-01, TARG-02, PAT-01, PAT-02, PAT-03, EXEC-01, EXEC-02, RPRT-01, RPRT-02
**Success Criteria** (what must be TRUE):

  1. User can pass a directory path and all files inside it are selected for renaming
  2. User can pass a glob pattern (e.g. `*.csv`) and only matching files are selected
  3. User can specify a wildcard remove pattern (e.g. `_{*}new_{*}`) and matching segments are stripped from filenames
  4. User can specify a replace transform (e.g. `.` to `-`) and substitutions are applied after all removes
  5. Running with --dry-run displays every before/after pair prefixed with `[DRY RUN]` and exits with zero files modified; running without --dry-run renames files and displays the same before/after pairs live, followed by a succeeded/failed summary count

**Plans**: 3 plansPlans:
**Wave 1**

- [x] 01-01-PLAN.md — Walking Skeleton: project scaffold, IFileSystem seam, WildcardCompiler, RenameCommand --help, E2E dry-run test

**Wave 2**

- [x] 01-02-PLAN.md — Full pipeline: FileDiscovery (glob), ReplaceTransform, operation order (removes→replaces), live execution

**Wave 3**

- [x] 01-03-PLAN.md — Reporter: per-file output formatting, summary counts, output validation, CommandAppTester CLI tests

**UI hint**: no

### Phase 2: Safety and Reporting

**Goal**: Users can trust that the tool will never silently corrupt or overwrite files — every conflict is resolved before any rename executes, failures are logged, and the help text shows exactly how to use the tool
**Mode:** mvp
**Depends on**: Phase 1
**Requirements**: SAFE-01, SAFE-02, RPRT-03, UX-01
**Success Criteria** (what must be TRUE):

  1. When two source files would compute to the same output name, the tool resolves the collision using Windows auto-numbering (`file(1).csv`, `file(2).csv`) before touching any file
  2. Running --dry-run with a batch that would have collisions shows the auto-numbered resolved names, confirming no file would be overwritten
  3. When any rename fails at runtime, a plain-text log file appears in the target directory listing each failed filename and its failure reason
  4. Running `filerevamp -help` displays usage instructions with at least one concrete wildcard pattern example and one replace example

**Plans**: TBD
**UI hint**: no

### Phase 3: Polish and Packaging

**Goal**: FileRevamp can be installed as a dotnet global tool and survives all documented edge cases — the tool is ready to publish and use in production batch workflows
**Mode:** mvp
**Depends on**: Phase 2
**Requirements**: (none — delivery and quality phase; all v1 requirements covered in Phases 1 and 2)
**Success Criteria** (what must be TRUE):

  1. `dotnet tool install -g FileRevamp` succeeds and `filerevamp --version` runs from any shell prompt
  2. Integration tests cover the full rename pipeline end-to-end against an in-memory file system with no temp directory management required
  3. The tool handles documented edge cases without crashing: filenames with literal dots and parentheses in wildcard patterns, output names that collide within the same batch, and the log file itself is never included as a rename target

**Plans**: TBD
**UI hint**: no

## Progress

**Execution Order:**
Phases execute in numeric order: 1 → 2 → 3

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Core Rename Pipeline | 3/3 | Complete | 2026-05-31 |
| 2. Safety and Reporting | 0/? | Not started | - |
| 3. Polish and Packaging | 0/? | Not started | - |
