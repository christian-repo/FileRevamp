# Domain Pitfalls: Batch File Rename CLI (.NET C#)

**Domain:** Batch file rename CLI tool
**Project:** FileRevamp
**Researched:** 2026-05-31

---

## Critical Pitfalls

Mistakes that cause data loss, silent corruption, or require a rewrite.

---

### Pitfall 1: Case-Only Rename Is a Silent No-Op on Windows

**What goes wrong:**
`File.Move` (which calls `MoveFileEx` internally) returns success but does nothing when the source
and destination filenames differ only in casing — e.g., `Report.CSV` → `report.csv`. The file stays
with its original casing. The user sees no error and no rename.

**Why it happens:**
NTFS is case-insensitive. Windows treats `Report.CSV` and `report.csv` as the same name in the same
directory, so the move is considered a no-op. `MoveFileEx` returns a success code despite not touching
the file. This is a documented driver-level limitation, not a .NET bug.

**Consequences:**
- Transforms that change only casing (e.g., lowercase replacements) silently fail.
- `--dry-run` output shows the rename; the live run does nothing. Users think the tool is broken.

**Prevention:**
Two-step rename for case-only changes: rename to a guaranteed-unique intermediate name first
(`filename.__tmp__`), then rename to the final destination. Always verify the rename took effect
by re-reading the directory entry after `File.Move`.

**Warning signs:**
- Replace operation that changes `.CSV` to `.csv` produces no visible change.
- Unit test passes on Linux/macOS CI but fails on Windows.

**Phase:** Core rename engine (Phase 1). Must be addressed before any replace operation is wired up.

---

### Pitfall 2: Auto-Numbering Cascade — `file(1)` Already Exists

**What goes wrong:**
Conflict resolution appends `(1)` to make a unique name. But if `file(1).csv` already exists in the
directory, the loop must try `(2)`, `(3)`, etc. If not implemented as a proper loop with a pre-built
set of existing names, the tool either throws, produces a duplicate, or enters an infinite loop.

A subtler variant: two files in the same batch both produce the same computed name. The first gets
`file(1).csv`, the second also tries `file(1).csv` (because the conflict-check snapshot was taken at
batch start, before the first rename). The tool overwrites its own rename output.

**Why it happens:**
Conflict detection is commonly done by calling `File.Exists` at rename time. This races against the
batch's own earlier renames. The snapshot of "what names exist" must include both the original
directory state AND all names already emitted by the current batch.

**Consequences:**
Files silently overwrite each other. The user asked for safe auto-numbering and loses data anyway.

**Prevention:**
Build a `HashSet<string>` of occupied names at batch-start (case-insensitive, to match NTFS). As
each rename is committed (or dry-run planned), add the output name to the set. The conflict-number
loop increments against this live set, not the filesystem.

**Warning signs:**
- Two source files have names that differ only by content that gets removed/replaced.
- Directory already contains `file(1).csv` before the tool runs.

**Phase:** Conflict resolution logic (Phase 1). Must be correct before wiring up the rename loop.

---

### Pitfall 3: Reserved Windows Filenames Are Not Caught by `Path.GetInvalidFileNameChars()`

**What goes wrong:**
A transform produces a name like `NUL`, `CON`, `AUX`, `PRN`, `COM1`–`COM9`, `LPT1`–`LPT9`
(or those names plus any extension, e.g., `NUL.csv`). `File.Move` throws `IOException` with a
cryptic parameter error. `Path.GetInvalidFileNameChars()` does NOT include these reserved names,
so standard BCL validation misses them entirely.

**Why it happens:**
These names are DOS device aliases kept for backward compatibility. Windows maps them to system
devices regardless of extension. They are not in the "invalid characters" list because the characters
themselves are valid — only those specific combinations are forbidden.

**Consequences:**
Unhandled exception during the rename loop. Other files in the batch that ran before the error are
renamed; those after are not. Partial batch with no rollback.

**Prevention:**
Validate every computed output filename against the full reserved-name list before entering the rename
phase. Check both the bare name and the stem (strip extension) against the set. Flag as a conflict
error, log it, and skip — do not attempt the `File.Move`.

Reserved names to check (case-insensitive):
`CON PRN AUX NUL COM0 COM1 COM2 COM3 COM4 COM5 COM6 COM7 COM8 COM9 LPT0 LPT1 LPT2 LPT3 LPT4 LPT5 LPT6 LPT7 LPT8 LPT9`

**Warning signs:**
- Source filenames contain substrings like `nul-export`, `aux_data` that a remove operation could
  reduce to just `nul` or `aux`.

**Phase:** Output-name validation (Phase 1), executed before the rename is attempted.

---

### Pitfall 4: Wildcard-to-Regex Conversion Errors in the Custom `{*}/{+}/{?}` Syntax

**What goes wrong:**
The project uses a custom wildcard syntax (`{*}` = `.*`, `{+}` = `.+`, `{?}` = `.?`). When
translating to a regex, filename characters that are regex metacharacters (`.`, `(`, `)`, `[`, `]`,
`+`, `^`, `$`) must be escaped first. If the conversion escapes wildcards before escaping
metacharacters, the braces get escaped as `\{` and the substitution breaks. If it escapes
metacharacters after substitution, the inserted `.*` gets double-escaped to `\.\*`.

A second bug: without `^...$` anchoring, the pattern `_{*}` matches any filename containing an
underscore followed by anything — including `prefix_something_suffix`, where the user expected only
filenames starting with `_`.

**Why it happens:**
Correct order is: escape all regex metacharacters in the raw pattern string first (`Regex.Escape`),
then substitute the escaped placeholder tokens with regex equivalents. Developers often reverse this
or forget anchoring because they are thinking about the semantics, not the string-transform order.

**Consequences:**
Patterns match too broadly or not at all. A remove of `_{*}` silently removes more than expected
("I only wanted to remove the trailing suffix!"). Discovered at demo time, not at build time.

**Prevention:**
Implement conversion as an explicit ordered pipeline:
1. `Regex.Escape(rawPattern)` — escapes `.+?()[]{}^$|` in the literal parts
2. Replace `\{\*\}` → `(.*)`, `\{\+\}` → `(.+)`, `\{\?\}` → `(.*?)` (non-greedy optional)
3. Wrap result in `^...$` anchors

Write a table-driven unit test suite covering: literal dots, parentheses in filenames, patterns at
start/middle/end of name, patterns with no match, and patterns that match the full name.

**Warning signs:**
- Test file `report.v2.(final).csv` matches a pattern it should not.
- Removing `_{*}` from `export_2024_data.csv` removes `_2024_data` rather than just `_data` or
  produces no match.

**Phase:** Pattern engine (Phase 1). Must have dedicated unit tests before integration.

---

## Moderate Pitfalls

---

### Pitfall 5: Long Paths Exceeding 260 Characters (MAX_PATH)

**What goes wrong:**
`Directory.EnumerateFiles` and `File.Move` throw `PathTooLongException` when the absolute path of a
source or destination file exceeds 260 characters. This is the default `MAX_PATH` limit on Windows.
Power users processing nested export directories frequently hit this. The `--recursive` flag makes it
much more likely because subdirectory depth multiplies path length.

**Why it happens:**
Windows has supported long paths since Windows 10 1607 via a registry key
(`HKLM\SYSTEM\CurrentControlSet\Control\FileSystem\LongPathsEnabled = 1`) and an application manifest
declaration (`<longPathAware>true</longPathAware>`), but .NET apps must explicitly opt in.

**Prevention:**
Add `<longPathAware>true</longPathAware>` to the application manifest. In the `.csproj`, set
`<ApplicationManifest>` to point to a manifest file with that element. As a belt-and-suspenders
fallback, prefix constructed absolute paths with `\\?\` when calling Win32-bound APIs.
Catch `PathTooLongException` gracefully: log the file as a failure with a human-readable message,
continue the batch.

**Warning signs:**
- `--recursive` on a deeply nested project export directory throws exceptions on the first run.
- Path length of source directory itself is already 180+ characters.

**Phase:** File enumeration / `--recursive` support (Phase 2).

---

### Pitfall 6: Trailing Dots and Trailing Spaces in Computed Filenames

**What goes wrong:**
A replace operation (e.g., replace `_` with `.`) can produce a filename ending in a dot:
`export_.csv` → `export..csv` → after further cleanup → `export.`. Windows allows this in NTFS via
the `\\?\` namespace but the standard Win32 API silently strips the trailing dot, creating a
mismatch between the name the tool attempts to write and the name the filesystem stores.
Similarly, a remove operation can leave trailing spaces that are silently stripped by Explorer but
create `File.Exists` inconsistencies.

**Prevention:**
After computing an output filename, `Trim()` trailing spaces and `TrimEnd('.')` before the dot-only
extension-stripping check. Add a validation rule: if the trimmed output name is empty or consists
only of dots/spaces, classify it as an error (not a rename candidate), log it, and skip.

**Warning signs:**
- Source file has a name like `data_.csv` where the underscore is the only non-extension character
  in the stem.

**Phase:** Output-name validation (Phase 1).

---

### Pitfall 7: Log File Write Fails When Target Directory Is Read-Only or Lacks Write Permission

**What goes wrong:**
The spec places the log file in the target directory. If the user runs FileRevamp against a directory
they have read and execute permission on but not write permission (e.g., a mounted network share,
a CD/DVD path, or a directory owned by another user), the rename phase succeeds but the log write
throws `UnauthorizedAccessException`, and the failure report is lost.

A second scenario: the tool itself produces failures, tries to write the log, fails writing the log,
and now the user has no record of what went wrong.

**Prevention:**
At startup, before the rename phase begins, check write permission on the target directory by
attempting to create and immediately delete a temp probe file. If that fails, emit a console warning:
"Cannot write log to [dir]. Log will be written to [fallback]." Fallback order:
1. `%TEMP%\FileRevamp\` (user temp)
2. The current working directory
3. Console-only output with an explicit note

Write the log path used to the summary line at the end of every run.

**Warning signs:**
- Running against a mapped network drive (`Z:\exports\`).
- Process is not running as the directory owner.

**Phase:** Logging infrastructure (Phase 1). Must be validated before any error is actually logged.

---

### Pitfall 8: `Directory.EnumerateFiles` Throws Mid-Enumeration on Access-Denied Files

**What goes wrong:**
When `--recursive` is used, `Directory.EnumerateFiles` with `SearchOption.AllDirectories` throws
`UnauthorizedAccessException` the moment it enters a subdirectory the current user cannot read
(e.g., `System Volume Information`, hidden OS folders). The entire enumeration aborts, not just that
subtree.

**Prevention:**
Do not pass `SearchOption.AllDirectories` directly. Instead, implement manual recursive enumeration:
enumerate the top directory, then enumerate children with individual try/catch blocks per subdirectory.
Log inaccessible directories as warnings, not errors, and continue with the rest of the tree.
`Directory.EnumerateFiles` is preferred over `Directory.GetFiles` (the latter loads all paths into
memory first; the former streams, which matters for directories with 50,000+ files).

**Warning signs:**
- Using `--recursive` on `C:\Users` or any system-managed directory.

**Phase:** `--recursive` support (Phase 2).

---

## Minor Pitfalls

---

### Pitfall 9: Regex Mode (`--advanced`) — User Supplies Pattern Without Anchors

**What goes wrong:**
A user passes `--advanced` with the pattern `\d{4}` intending to match filenames that are exactly
four digits. Without `^...$` anchoring, the regex matches any filename containing four consecutive
digits — including `report_2024_final.csv`. All such files are renamed, far more than intended.

**Prevention:**
In `--advanced` mode, do NOT silently add anchors (the user is in expert mode and may intentionally
want substring matching for the match step). Instead, document clearly in `--help` output and
examples that patterns match the full filename by default when `^...$` is present, or anywhere in
the name when anchors are absent. Include an anchored example prominently in the help text.
Optionally, emit a console warning if the pattern contains no anchors: "Pattern has no anchors.
It will match any filename containing this substring. Use --dry-run to preview."

**Warning signs:**
- User reports "it renamed way more files than I expected."

**Phase:** `--advanced` mode / help text (Phase 1 or 2).

---

### Pitfall 10: Unicode Normalization Mismatch Between Pattern and Filename

**What goes wrong:**
Filenames copied from macOS or Linux may use NFD (decomposed) Unicode: the character `é` is stored
as `e` + combining acute accent (two code points). The user types the pattern with NFC `é` (one code
point). `string.Contains` and `Regex` in .NET compare by ordinal code-point value by default, so
they will not match despite looking identical in the console.

**Why it happens:**
Windows NTFS stores filenames in UTF-16 but does not normalize them. Files created on macOS (HFS+
uses NFD) or copied via SMB retain their original normalization. .NET string comparison is
code-point-by-code-point unless `StringComparison.CurrentCultureIgnoreCase` or explicit normalization
is used.

**Prevention:**
Normalize both the filename and the pattern to NFC before comparison and before applying regex:
`filename.Normalize(NormalizationForm.FormC)`. This is a one-liner addition to the matching step.
Document that patterns are matched against NFC-normalized filenames.

**Warning signs:**
- Pattern does not match filenames copied from a Mac share even though they visually look identical.

**Phase:** Pattern-matching engine (Phase 1), a single normalization call.

---

### Pitfall 11: Glob Pattern Passed Where Wildcard Syntax Is Expected (User Confusion)

**What goes wrong:**
Users familiar with shell globbing pass patterns like `*_new_*` expecting the tool's wildcard syntax.
The tool expects `{*}_new_{*}`. The bare `*` is not a valid token in the custom syntax — depending
on implementation it either matches nothing or throws. The user gets no renames and no clear error
explaining the mismatch.

**Prevention:**
In the parser, detect unbraced `*` or `?` in wildcard mode and emit a targeted error:
"Did you mean `{*}` instead of `*`? Use --advanced for standard regex." Include a glob-to-custom-
syntax mapping table in the `--help` output. This is a UX issue, not a bug — but it will generate
the majority of first-use support requests.

**Warning signs:**
- No renames occur on the first user run despite a pattern that looks correct.

**Phase:** Argument parsing / `--help` text (Phase 1).

---

### Pitfall 12: Log File Is Itself Matched by the Rename Pattern

**What goes wrong:**
The tool creates a log file (e.g., `filerevamp-errors.log`) in the target directory. If the rename
pattern matches `.log` files and the log file is created before enumeration, or if enumeration
includes it, the tool may attempt to rename its own log file mid-run, causing an `IOException`
(file locked by the current process) or a confusing log entry about the log file itself.

**Prevention:**
Enumerate all candidate files before opening the log file (collect file paths into a list first,
then open the log, then process the list). Alternatively, always exclude the log file by its
well-known name from the candidate list. Since enumeration and log creation are sequential, the
simpler fix is: enumerate → collect list → open log → rename loop.

**Warning signs:**
- Pattern targets all files or targets files by extension that includes `.log`.

**Phase:** Main run loop ordering (Phase 1).

---

## Phase-Specific Warnings

| Phase Topic | Likely Pitfall | Mitigation |
|---|---|---|
| Core rename engine | Case-only rename silent no-op (Pitfall 1) | Two-step rename via temp name |
| Conflict resolution | Auto-number cascade / same-batch collision (Pitfall 2) | Live HashSet of occupied names |
| Output-name validation | Reserved names, trailing dots/spaces (Pitfalls 3, 6) | Pre-rename validation pass |
| Pattern engine — wildcard | Wrong conversion order, missing anchors (Pitfall 4) | Table-driven unit tests |
| Pattern engine — advanced | Substring match surprises (Pitfall 9) | Warn on anchor-free patterns |
| Logging infrastructure | Read-only target directory (Pitfall 7) | Probe write permission at startup |
| Run loop ordering | Log file caught by its own pattern (Pitfall 12) | Enumerate before opening log |
| --recursive support | EnumerateFiles aborts on access-denied subtree (Pitfall 8) | Manual recursive loop with per-dir catch |
| --recursive support | Long paths exceeding MAX_PATH (Pitfall 5) | Manifest longPathAware + graceful catch |
| First-time UX | Glob vs custom wildcard confusion (Pitfall 11) | Parser detects bare `*`, emits targeted error |
| Cross-platform files | Unicode NFD/NFC mismatch (Pitfall 10) | NFC-normalize before matching |

---

## Sources

- [Reserved filenames on Windows — Meziantou's blog](https://www.meziantou.net/reserved-filenames-on-windows-con-prn-aux-nul.htm) — HIGH confidence (official BCL behavior documented)
- [Naming Files, Paths, and Namespaces — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/fileio/naming-a-file) — HIGH confidence (official Win32 docs)
- [Maximum Path Length Limitation — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/fileio/maximum-file-path-limitation) — HIGH confidence
- [MoveFileEx case-sensitivity bug — vadcx/movefileex-casesensitivity](https://github.com/vadcx/movefileex-casesensitivity) — MEDIUM confidence (reproduced community report, driver-level limitation)
- [Workaround for Windows CASE issue — allanhutchison.net](https://allanhutchison.net/2025/06/24/workaround-for-windows-case-issue-when-renaming-files/) — MEDIUM confidence (corroborates two-step workaround)
- [Directory.EnumerateFiles performance issue — dotnet/runtime #31214](https://github.com/dotnet/runtime/issues/31214) — MEDIUM confidence (filed against .NET Core 3.0; check current version behaviour)
- [How to Enumerate Directories and Files — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/standard/io/how-to-enumerate-directories-and-files) — HIGH confidence
- [Wildcard to Regex conversion pitfalls — codestudy.net](https://www.codestudy.net/blog/need-to-perform-wildcard-etc-search-on-a-string-using-regex/) — MEDIUM confidence (consistent with official Regex.Escape docs)
- [Using Unicode Normalization — Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/intl/using-unicode-normalization-to-represent-strings) — HIGH confidence
- [Numbering conflicts in batch rename — Advanced Renamer forum](https://www.advancedrenamer.com/forum_thread/renumbering-managing-conflicts-of-files-with-the-same-name-14807) — LOW confidence (community forum, illustrates pattern)
- [Glob vs. Regex — blog.apify.com](https://blog.apify.com/glob-vs-regex/) — MEDIUM confidence (illustrates documented user confusion)
