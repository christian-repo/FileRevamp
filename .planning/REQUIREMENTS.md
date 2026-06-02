# Requirements: FileRevamp

**Defined:** 2026-05-31
**Core Value:** A file renaming operation must be predictable and reversible: show the user exactly what will happen before doing it, and never silently corrupt filenames.

## v1 Requirements

### Targeting

- [x] **TARG-01**: User can pass a directory path to rename all files inside it
- [x] **TARG-02**: User can pass a glob pattern (e.g. `*.csv`) to select files in the current directory

### Pattern Matching

- [x] **PAT-01**: User can specify a remove operation using simplified wildcard syntax with brace-token quantifiers (`{*}` = any chars, `{+}` = one or more, `{?}` = zero or one) — e.g. `_{*}new_{*}` removes `____new___` segments from filenames
- [x] **PAT-02**: User can specify a replace/transform operation to substitute characters or substrings — e.g. `.` → `-`
- [x] **PAT-03**: Multiple operations apply in fixed order: all remove operations first, then all replacement operations, in the order passed on the command line

### Execution

- [x] **EXEC-01**: User can preview all renames with `--dry-run` — displays before/after pairs without touching any file
- [x] **EXEC-02**: Without `--dry-run`, tool executes all renames after pre-flight validation passes

### Safety

- [ ] **SAFE-01**: Tool validates the entire rename batch before touching any file (pre-flight); no file is renamed if any collision is detected
- [ ] **SAFE-02**: When the computed output name already exists, tool auto-numbers the new name using Windows convention — e.g. `file(1).csv`, `file(2).csv`

### Output & Reporting

- [x] **RPRT-01**: Tool displays each rename as it runs — source filename → destination filename (or `[DRY RUN]` prefix in dry-run mode)
- [x] **RPRT-02**: At end of run, tool displays a summary: total files processed, succeeded, and failed
- [ ] **RPRT-03**: On any failure, tool creates a log file in the target directory listing each failed filename and the reason

### User Experience

- [ ] **UX-01**: User can run `filerevamp -help` to display usage instructions and pattern examples

## v2 Requirements

### Advanced Targeting

- **TARG-03**: User can recurse into subdirectories with `--recursive` flag (introduces MAX_PATH and access-denied isolation edge cases — defer)

### Advanced Patterns

- **PAT-04**: User can specify full regex patterns via `--advanced` flag for power users comfortable with regex
- **PAT-05**: In `--advanced` mode, tool warns when pattern has no anchors (`^`/`$`) which may cause unexpected over-matches

### Safety Hardening

- **SAFE-03**: Tool validates output filenames for Windows reserved names (`NUL`, `CON`, `AUX`, `COM1-9`, `LPT1-9`), forbidden characters, and trailing dots/spaces
- **SAFE-04**: Case-only renames (same name, different casing) use a two-step rename through a temp name to avoid NTFS silent no-op

### Differentiating Features

- **FEAT-01**: Sequential zero-padded auto-numbering for batch output naming (e.g. `file_001.csv`)
- **FEAT-02**: Case transforms — `--upper`, `--lower`, `--title` applied to filename
- **FEAT-03**: Extension normalization — standardize file extension casing

## Out of Scope

| Feature | Reason |
|---------|--------|
| GUI / interactive UI | Command-line only; scripting is the core use case |
| Undo / rollback | Use `--dry-run` before committing; rollback adds significant complexity |
| Directory renaming | Ordering hazards during recursive traversal; files only |
| Cross-drive file moves | Rename within same filesystem only |
| Config file presets | Explicit command-line args are simpler and more scriptable |
| Plugin architecture | Adds complexity with no clear v1 need |
| EXIF / ID3 metadata renaming | Out of domain for a filename-only tool |

## Traceability

Updated during roadmap creation — 2026-05-31.

| Requirement | Phase | Status |
|-------------|-------|--------|
| TARG-01 | Phase 1 | Complete |
| TARG-02 | Phase 1 | Complete |
| PAT-01 | Phase 1 | Complete |
| PAT-02 | Phase 1 | Complete |
| PAT-03 | Phase 1 | Complete |
| EXEC-01 | Phase 1 | Complete |
| EXEC-02 | Phase 1 | Complete |
| RPRT-01 | Phase 1 | Complete |
| RPRT-02 | Phase 1 | Complete |
| SAFE-01 | Phase 2 | Pending |
| SAFE-02 | Phase 2 | Pending |
| RPRT-03 | Phase 2 | Pending |
| UX-01 | Phase 2 | Pending |

**Coverage:**
- v1 requirements: 13 total
- Mapped to phases: 13
- Unmapped: 0 ✓

---
*Requirements defined: 2026-05-31*
*Last updated: 2026-05-31 after roadmap creation*
