---
status: complete
phase: 01-core-rename-pipeline
source: [01-01-SUMMARY.md, 01-02-SUMMARY.md, 01-03-SUMMARY.md]
started: 2026-06-01T00:00:00Z
updated: 2026-06-01T01:00:00Z
---

## Current Test

[testing complete]

## Tests

### 1. Help / CLI launch
expected: |
  Run: dotnet run --project src/FileRevamp -- --help
  The tool prints a usage block that includes:
    --remove <pattern>  (or similar)
    --replace <operand>
    --dry-run
  Exit code is 0. No crash or stack trace.
result: pass

### 2. Dry-run preview — no files changed
expected: |
  Set up a temp folder with 2–3 files (e.g., _foo_new_bar.csv, _baz_new_qux.txt).
  Run: dotnet run --project src/FileRevamp -- <tempdir> --remove "_new" --dry-run
  Each matching file shows a "[DRY RUN]" line (e.g., "[DRY RUN] _foo_new_bar.csv → _foo_bar.csv").
  After the command completes, the files on disk are UNCHANGED — original names remain.
result: pass

### 3. Dry-run summary format
expected: |
  Immediately after Test 2 (or any --dry-run run with matching files), the final summary line reads:
  "Dry run complete — N would be renamed, M skipped, K failed. 0 files modified."
  (with actual numbers, not placeholders). "0 files modified" appears at the end.
result: pass

### 4. Wildcard remove — middle segment
expected: |
  With a file named _foo_new_bar.csv in a temp folder, run:
  dotnet run --project src/FileRevamp -- <tempdir> --remove "_{*}new_{*}" --dry-run
  The [DRY RUN] line shows: _foo_new_bar.csv → _foo_.csv
  The middle "new_bar" portion is removed; the leading underscore and stem prefix remain.
result: pass

### 5. Literal replace — find->replacement
expected: |
  With files named report_2024.csv and report_2025.csv, run:
  dotnet run --project src/FileRevamp -- <tempdir> --replace "report->summary" --dry-run
  The [DRY RUN] lines show both files renamed:
    report_2024.csv → summary_2024.csv
    report_2025.csv → summary_2025.csv
  Exit code 0.
result: pass

### 6. Live rename — files actually renamed
expected: |
  With a file named old_name.txt in a temp folder, run WITHOUT --dry-run:
  dotnet run --project src/FileRevamp -- <tempdir> --replace "old->new"
  The output shows: old_name.txt → new_name.txt  (no [DRY RUN] prefix)
  The summary shows "Renamed: 1  Failed: 0  Skipped: 0"
  On disk, old_name.txt is gone and new_name.txt exists.
result: pass

### 7. Invalid --replace operand exits non-zero
expected: |
  Run: dotnet run --project src/FileRevamp -- . --replace "nodash"
  (the operand is missing the -> separator)
  The tool prints an error message about the invalid operand and exits with code 1.
  No files are renamed. Echo $LASTEXITCODE (PowerShell) or $? should confirm non-zero.
result: pass

### 8. Empty-stem protection
expected: |
  With a file named _new.csv, run:
  dotnet run --project src/FileRevamp -- <tempdir> --remove "_new" --dry-run
  The tool does NOT show a [DRY RUN] rename to .csv.
  Instead it shows an error/skip for that file (e.g., "empty stem" or "skipped").
  The file remains _new.csv on disk.
result: pass

## Summary

total: 8
passed: 8
issues: 0
pending: 0
skipped: 0
blocked: 0

## Gaps

[none yet]
