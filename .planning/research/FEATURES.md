# Feature Landscape

**Domain:** Batch file rename CLI tool (.NET C# console)
**Researched:** 2026-05-31
**Confidence:** HIGH for core feature categorization (cross-validated across rnr, F2, BRU, Advanced Renamer, rename-cli); MEDIUM for complexity estimates

---

## Table Stakes

Features users expect in any serious rename tool. Missing = product feels broken or untrustworthy.

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| Dry-run / preview mode | Every established tool ships this (rnr defaults to it, F2 defaults to it, renamer docs say "always run --dry-run first"). Users will not trust a batch rename tool that touches files without preview. | Low | FileRevamp has `--dry-run`. Non-negotiable. |
| Before/after listing in console output | Users need to see original → new name for every file, inline, before and after commit. Character-level diffs reduce validation time. | Low | Must be human-readable. `old.csv → new.csv` format is standard. |
| Summary count at end of run | Count of succeeded / failed / skipped. Users scan for "0 failures" before moving on. | Low | FileRevamp already specifies this. |
| Conflict detection before execution | Check that no two files would rename to the same target. Silent collision = data loss. F2, brename, rnr all do pre-flight validation. | Medium | Must happen before touching any file, not mid-run. |
| Conflict resolution — auto-number | Append `(1)`, `(2)` to colliding targets rather than overwriting. Windows convention; users recognize it. | Low-Medium | FileRevamp chooses auto-number. Skip and overwrite are alternatives but auto-number is safest default. |
| Error logging to file | When a rename fails (permissions, locked file, path too long), write filename + reason to a log. Essential for auditing bulk CSV export workflows. | Low | Log goes in target directory per FileRevamp spec. |
| Glob / wildcard target selection | `*.csv`, `report_*`, `**/*.txt`. Users pass file sets by pattern, not individual filenames. | Low | Use `Microsoft.Extensions.FileSystemGlobbing` (BCL-adjacent NuGet); no external dep cost. |
| Recursive directory traversal | `--recursive` flag. Processing exports in nested folders is a standard case. | Low | FileRevamp already specifies this. |
| Help / usage text | `-help` flag with examples. First thing a new user runs. | Low | Include concrete pattern examples, not just flag descriptions. |
| Regex pattern matching | Full regex for advanced users. All serious CLI rename tools support regex (rnr, F2, rename, brename, nomino). | Medium | FileRevamp gates this behind `--advanced`. Correct design. |
| String remove / replace operations | Core transform verbs. Remove a substring, replace one string with another. | Low | Fixed order (remove first, then replace) per FileRevamp spec. |
| Path-safe output validation | Detect forbidden characters for the target OS (`\/:*?"<>|` on Windows), names exceeding MAX_PATH, trailing periods (Windows rejects them). F2 explicitly checks all of these. | Medium | Windows-first tool — must guard against Windows-specific invalids. |

---

## Differentiators

Features that set a rename tool apart. Not universally expected, but users notice and value them.

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| Custom wildcard syntax (simplified regex) | Lowers barrier for non-regex users. `_{*}new_{*}` is readable without regex knowledge. None of the surveyed tools offer this — they all go straight to regex. | Medium | FileRevamp's `{*}` / `{+}` / `{?}` syntax is a genuine differentiator for the developer/power-user-but-not-regex-expert segment. |
| Fixed operation order as a mental model guarantee | Removes ambiguity about which transforms apply first. Tools like BRU apply operations in UI order — confusing. A stated, documented order is a trust feature. | Low | FileRevamp's "removes first, then replacements" is simple and teachable. Document it prominently in help text. |
| Targeted log file co-location | Log lives next to the renamed files. No hunting in `%APPDATA%` or `/var/log`. Obvious for the "bulk CSV exports" use case. | Low | Already in spec. Good decision. |
| Per-file success/failure inline output | Show each rename result as it happens, not just a final summary. Users can interrupt and see partial state. | Low | Standard in good CLI tools; not universal in rename tools. |
| Explicit "no files modified" guarantee in dry-run | Dry-run that clearly states "0 files changed" in its output. Users need reassurance, not just silence. | Low | One output line: `Dry run complete — 0 files modified.` |
| Sequential auto-numbering with padding | `file_001.csv`, `file_002.csv`. Useful for export batches where alphabetical sort order matters. Surveyed tools (BRU, Advanced Renamer, F2) support configurable padding. | Medium | Not in current FileRevamp spec. Worth considering as a Phase 2 differentiator — adds predictable naming for downstream automation. |
| Case transforms (upper, lower, title) | Normalize inconsistent casing from system-generated names. Offered by F2, BRU, Advanced Renamer, rnr (`-t upper`). | Low | Not in current spec. Low-complexity addition with real utility for CSV export normalization. |
| Extension normalization | Lowercase `.CSV` to `.csv`. Common for files from Windows system exports. Subcase of case transforms. | Low | Could be a specific `--normalize-ext` flag — trivial to add. |

---

## Anti-Features

Features to deliberately NOT build for a CLI rename tool targeting developers and power users.

| Anti-Feature | Why Avoid | What to Do Instead |
|--------------|-----------|-------------------|
| Interactive TUI / wizard mode | Violates CLI composability. Cannot be scripted or piped. Adds significant complexity for zero portability benefit. Tools like massren and edir (editor-based) serve interactive users — FileRevamp should not. | Invest in clear `--help` examples and dry-run preview |
| GUI application | Out of scope (per PROJECT.md), but worth stating: a GUI increases maintenance surface by 10x for marginal user gain in this segment. Power users want scriptability. | Excellent console output with color is the right substitute |
| Undo / rollback of completed renames | Stateful undo (like rnr's dump files) implies storing rename history, managing state files, handling edge cases when files move between undo. High complexity, low value when `--dry-run` exists. PROJECT.md correctly excludes this. | `--dry-run` is the undo prevention strategy |
| Metadata-driven renaming (EXIF, ID3 tags) | Photo/music workflows are a different market. Adds parsing dependencies, edge cases, and complexity that dilutes the tool's focus. BRU and Advanced Renamer serve this — FileRevamp should not. | Out of scope — document it explicitly so users know |
| Move files between directories | Separate concern from renaming. Introduces cross-device risk, permission surface, and rollback complexity. PROJECT.md correctly excludes cross-drive moves. | Stay within rename-in-place |
| Directory renaming | Renaming directories while recursing into them creates ordering hazards (parent renamed before child paths resolve). PROJECT.md correctly excludes this. | Files only |
| Config file / preset saving | Encourages state accumulation. A CLI tool should be fully described by its arguments. Presets belong in shell aliases or scripts, not in the tool. | Document shell alias patterns in help text |
| Plugin / extension architecture | Over-engineering for a focused tool. renamer (Node.js) takes this approach and the plugin ecosystem is fragmented and unmaintained. | Hardcode the supported transforms; add more in versions |
| Silent failure | Any failure to rename a file must be visible — either inline or in the log. Silent drops are the single worst behavior in a batch tool. | Log every failure; never swallow errors |

---

## Feature Dependencies

```
Glob target selection
  └── Recursive traversal (--recursive expands glob scope into subdirs)
       └── File list assembly
            ├── Pattern matching (wildcard or --advanced regex)
            │    └── Transform computation (remove + replace)
            │         └── Conflict detection (pre-flight, before any I/O)
            │              ├── Dry-run output (if --dry-run: print and stop)
            │              └── Rename execution
            │                   ├── Per-file success/failure inline output
            │                   ├── Error log (on any failure)
            │                   └── Summary count
            └── Path validation (forbidden chars, MAX_PATH, trailing period)
```

Key ordering constraint: conflict detection and path validation must complete before execution begins — not interleaved with renames. This prevents partial-state bugs where 50 files rename successfully before hitting a conflict on file 51.

---

## MVP Recommendation

The current FileRevamp spec already maps cleanly to a solid MVP. Prioritize in this order:

**Must ship (table stakes):**
1. Glob target selection + recursive traversal
2. Wildcard pattern syntax (`{*}`, `{+}`, `{?}`) and regex (`--advanced`)
3. Remove + replace transforms, fixed order
4. Pre-flight conflict detection + auto-number resolution
5. Dry-run with clear "0 files modified" confirmation output
6. Per-file before/after output + end-of-run summary count
7. Error log in target directory on any failure
8. Path-safe output validation (Windows forbidden chars + MAX_PATH)
9. `-help` with examples

**Defer to Phase 2 (differentiators worth considering):**
- Sequential padding for auto-numbering (`file_001.csv`)
- Case transforms (`--upper`, `--lower`, `--title`)
- Extension normalization (`--normalize-ext`)

**Do not build:**
- Undo/rollback, directory renaming, metadata renaming, GUI, TUI, presets, plugins

---

## Sources

- rnr (Rust): https://github.com/ismaelgv/rnr — dry-run-by-default design, dump-file undo, UTF-8 validation
- F2 (Go): https://github.com/ayoisaiah/f2 / https://f2.freshman.tech — conflict detection taxonomy, forbidden-char checks, MAX_PATH validation, undo, dry-run default
- Bulk Rename Utility: https://www.bulkrenameutility.co.uk / https://bulk-rename-utility.fileion.com/articles/bulk-rename-utility-features-guide — metadata renaming, auto-number, undo batch file, log file
- Advanced Renamer: https://www.advancedrenamer.com — 14 rename methods, real-time preview, batch undo, metadata scope
- rename-cli (Node): https://www.npmjs.com/package/rename-cli — regex variables, date/metadata injection, pattern syntax
- renamer (Node): https://www.npmjs.com/package/renamer — dry-run emphasis, plugin arch as anti-pattern evidence
- Awesome-Rename-Tools: https://github.com/ugzv/Awesome-Rename-Tools — ecosystem survey (brename, detox, edir, F2, massren, mmv, nomino, rnr)
- Renamed.to blog: https://www.renamed.to/blog/build-scalable-file-renaming-ops — before/after log design, collision detection before execution
- Renamed.to UX data: https://www.renamed.to/blog/choose-the-right-file-renaming-tool — preview trust metric (3.1x decision latency reduction), visual diff value
- Microsoft.Extensions.FileSystemGlobbing: https://learn.microsoft.com/en-us/dotnet/core/extensions/file-globbing — BCL glob library
