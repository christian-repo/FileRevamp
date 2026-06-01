---
phase: 01-core-rename-pipeline
reviewed: 2026-05-31T00:00:00Z
depth: standard
files_reviewed: 22
files_reviewed_list:
  - src/FileRevamp/Commands/RenameCommand.cs
  - src/FileRevamp/Commands/RenameSettings.cs
  - src/FileRevamp/Core/DryRunFileSystem.cs
  - src/FileRevamp/Core/FileDiscovery.cs
  - src/FileRevamp/Core/FileSystem.cs
  - src/FileRevamp/Core/IFileSystem.cs
  - src/FileRevamp/Core/MockFileSystem.cs
  - src/FileRevamp/Core/RenameOrchestrator.cs
  - src/FileRevamp/Core/ReplaceTransform.cs
  - src/FileRevamp/Core/WildcardCompiler.cs
  - src/FileRevamp/Core/WildcardPatternMatcher.cs
  - src/FileRevamp/Infrastructure/TypeRegistrar.cs
  - src/FileRevamp/Output/RenameResult.cs
  - src/FileRevamp/Output/Reporter.cs
  - src/FileRevamp/Program.cs
  - tests/FileRevamp.Tests/Commands/RenameCommandTests.cs
  - tests/FileRevamp.Tests/Core/FileDiscoveryTests.cs
  - tests/FileRevamp.Tests/Core/RenameOrchestratorTests.cs
  - tests/FileRevamp.Tests/Core/ReplaceTransformTests.cs
  - tests/FileRevamp.Tests/Core/WildcardCompilerTests.cs
  - tests/FileRevamp.Tests/Core/WildcardPatternMatcherTests.cs
  - tests/FileRevamp.Tests/Output/ReporterTests.cs
findings:
  critical: 3
  warning: 5
  info: 3
  total: 11
status: issues_found
---

# Phase 01: Code Review Report

**Reviewed:** 2026-05-31
**Depth:** standard
**Files Reviewed:** 22
**Status:** issues_found

## Summary

The core rename pipeline is structurally sound: Clean Architecture boundaries are respected,
the dry-run seam works correctly, and the TOCTOU race on file-exists checks is handled. All
53 tests pass. The most serious issues are a correctness bug in `BuildRemoveReplacement` (greedy
vs. non-greedy affects which part of a filename survives a remove), silent discard of invalid
`--replace` operands that returns exit code 0, and a missing validation that prevents empty
output stems (producing dotfiles like `.csv`). Below are the specific findings.

---

## Critical Issues

### CR-01: Invalid `--replace` operand silently discarded — exit code stays 0

**File:** `src/FileRevamp/Commands/RenameCommand.cs:68-83`

**Issue:** When `ReplaceTransform.Parse` throws `ArgumentException` (e.g., `--replace "nodash"`),
the exception is caught, an error message is printed, and the operand is filtered to `null`. The
command then proceeds with an empty replace list and returns exit code `0`. The user requested an
operation that was silently dropped; the tool pretends success.

A caller relying on the exit code for scripting (e.g., `filerevamp . --replace "x->y" && next_step`)
has no way to detect that the replace transformation was never applied.

**Fix:**
```csharp
// Track whether any replace operand failed to parse.
var replaceParseError = false;
var replaceTransforms = (settings.ReplaceOperations ?? Array.Empty<string>())
    .Select(op =>
    {
        try { return ReplaceTransform.Parse(op); }
        catch (ArgumentException ex)
        {
            _console.MarkupLine($"[red]Invalid --replace operand '{Markup.Escape(op)}': {Markup.Escape(ex.Message)}[/]");
            replaceParseError = true;
            return null;
        }
    })
    .Where(t => t is not null)
    .Cast<ReplaceTransform>()
    .ToList();

if (replaceParseError)
    return 1;
```

Alternatively, move `--replace` operand validation into `RenameSettings.Validate()` (same pattern as bare-asterisk detection) so Spectre.Console rejects the command before `Execute` is reached.

---

### CR-02: Greedy wildcard produces counterintuitive remove behavior for repeated literals

**File:** `src/FileRevamp/Core/WildcardCompiler.cs:47-49` and `src/FileRevamp/Core/WildcardCompiler.cs:65`

**Issue:** `ToRegex` uses `(.*)` (greedy) for all wildcard tokens, but `BuildRemoveReplacement`'s
doc comment says (line 65): *"regex `^_(.*?)new_(.*)$`"* — the `?` is non-greedy. The actual
generated regex is `^_(.*) new_(.*)$` (greedy). For a filename containing the literal twice, greedy
behavior produces an unexpected result.

**Concrete example:**

File: `_a_new_b_new_c.csv`, pattern: `_{*}new_{*}`

- Anchored greedy regex: `^_(.*) new_(.*)$`
- Greedy `(.*)` in group 1 captures `a_new_b_`, then literal `new_`, group 2 = `c`
- Replacement `_$1` = `_a_new_b_`
- Final filename: `_a_new_b_.csv`

Expected (non-greedy, matching first occurrence of `new_`):

- `^_(.*?)new_(.*)$` — group 1 = `a_`, literal `new_`, group 2 = `b_new_c`
- Replacement `_$1` = `_a_`
- Final filename: `_a_.csv`

The greedy result retains a second `new_` in the filename, which is almost certainly not what
the user intended when they asked to remove the `_{*}new_{*}` segment. The doc comment's
description of the expected behavior (non-greedy) is also wrong, which will mislead maintainers.

**Fix:** Change the replacement strings in `ToRegex` to use non-greedy quantifiers for the captures
that appear before a trailing literal segment. Concretely, replace `(.*)` with `(.*?)` for all
tokens *except* the final one:

```csharp
// Step 2: Replace escaped brace tokens — use non-greedy quantifiers so the first
// matching literal segment anchors the removal, not the last one.
var substituted = escaped
    .Replace(@"\{\*}", "(.*?)")   // was "(.*)": greedy over-captures with repeated literals
    .Replace(@"\{\+}", "(.+?)")   // consistency
    .Replace(@"\{\?}", "(.?)");
```

Note: changing to non-greedy is a behavioral change for all patterns. If the decision is to
keep greedy, update the doc comment to remove the `?` and add a note explaining that the LAST
occurrence of the literal delimiter is removed when the pattern appears multiple times.

---

### CR-03: Empty output stem produces dotfile (e.g., `.csv`) without error

**File:** `src/FileRevamp/Core/RenameOrchestrator.cs:87-92` and `src/FileRevamp/Output/Reporter.cs:51-63`

**Issue:** When all characters in the file's stem are removed by the pattern, the computed output
filename is just the extension — e.g., `.csv`. `ValidateOutputName` does not detect this case:
`.csv` is not empty, does not end with a dot, and does not end with a space. The file is then
renamed to `.csv`, creating a hidden file (Unix convention) or an extension-only file on Windows
that is inaccessible in many UI contexts.

**Concrete example:**

File: `_new.csv`, pattern: `_new`

- Stem: `_new`, extension: `.csv`
- Anchored regex `^_new$` matches entire stem
- `BuildRemoveReplacement("_new")` returns `""` (no wildcards)
- `matchRegex.Replace("_new", "")` → `currentStem = ""`
- `newFilename = "" + ".csv"` = `.csv`
- `ValidateOutputName(".csv")` returns null (no error)
- File is renamed to `.csv`

**Fix:** Add a check in `ValidateOutputName` (or in `WildcardPatternMatcher.ApplyRemoves`) for
an empty stem after extension is stripped:

```csharp
// In Reporter.ValidateOutputName or in RenameOrchestrator after computing newFilename:
if (string.IsNullOrEmpty(Path.GetFileNameWithoutExtension(filename)))
    return "Computed filename has empty stem (would produce extension-only file)";
```

Alternatively, check in `WildcardPatternMatcher.ApplyRemoves` that `currentStem` is not empty
before concatenating the extension, and return null (no match) if the stem would be fully erased.

---

## Warnings

### WR-01: `TypeRegistrar.RegisterLazy` evaluates the factory eagerly

**File:** `src/FileRevamp/Infrastructure/TypeRegistrar.cs:29-32`

**Issue:** The `RegisterLazy` contract advertises deferred construction, but the implementation
calls `factory()` immediately during registration:

```csharp
public void RegisterLazy(Type service, Func<object> factory)
{
    _instances[service] = factory();   // eager, not lazy
}
```

Spectre.Console.Cli calls `RegisterLazy` for services that may have expensive construction or
side effects. Evaluating them at registration time instead of first-use violates the contract,
moves potential exceptions from first-use to startup, and can cause startup failures for
registrations that expect a fully-built container.

**Fix:**
```csharp
public void RegisterLazy(Type service, Func<object> factory)
{
    // Defer evaluation to first Resolve() call; use Lazy<T> to evaluate at most once.
    var lazy = new Lazy<object>(factory);
    // Store a sentinel or use a separate _lazyInstances dictionary:
    _lazyInstances[service] = lazy;
}

// In Resolve():
if (_lazyInstances.TryGetValue(type, out var lazy))
    return lazy.Value;
```

---

### WR-02: Literal pattern that equals the full stem is silently matched via anchored path, causing `anyMatch=true` with no actual change detectable at the `ApplyRemoves` level

**File:** `src/FileRevamp/Core/WildcardPatternMatcher.cs:93-97`

**Issue:** When a **literal** pattern exactly matches the entire stem (e.g., file `_new.csv`,
pattern `_new`), the anchored regex `^_new$` matches, and `matchRegex.Replace` is called with
replacement `""`. This sets `anyMatch = true` and correctly erases the stem. The orchestrator then
encounters `newFilename = ".csv"` and, absent the CR-03 fix, proceeds to rename. This is the
mechanism behind CR-03, but independently, it reveals that the match/replace branching
(lines 93-103) doesn't distinguish between wildcard patterns and literal patterns. A literal
pattern on the full stem hits the anchored path, while a literal pattern on a substring hits the
unanchored path. Both are correct in isolation, but the `replacement` value returned by
`BuildRemoveReplacement` for a literal pattern is `""` (line 77: "No wildcard tokens — the
pattern is a pure literal; replacing with '' removes everything"), which means the anchored path
for a full-stem-matching literal pattern deletes the entire stem — not just the occurrence.

This can lead to data loss (CR-03) and also means the semantics differ silently based on whether
the literal substring happens to equal the full stem.

**Fix:** Add a guard in `ApplyRemoves` for empty stem after the anchored replacement:

```csharp
if (matchRegex.IsMatch(currentStem))
{
    var result = matchRegex.Replace(currentStem, replacement);
    if (result.Length == 0 && extension.Length > 0)
    {
        // Stem was entirely consumed — treat as a failed transform, not a valid rename.
        return null;   // or surface an error through a different return type
    }
    currentStem = result;
    anyMatch = true;
}
```

---

### WR-03: `ReplaceTransform` constructor does not validate that `Find` is non-empty

**File:** `src/FileRevamp/Core/ReplaceTransform.cs:25-29`

**Issue:** The constructor accepts `find = ""` without throwing. Calling `Apply` with an empty
`Find` string calls `filename.Replace("", replace, StringComparison.Ordinal)`. The
`String.Replace(String, String, StringComparison)` overload in .NET throws
`ArgumentException("String cannot be of zero length")` when `oldValue` is empty. This is an
unhandled exception from `Apply` in the orchestrator pipeline (caught only by the top-level
`catch (Exception ex)` inside the live-rename path, and NOT caught at all if the replace
transform fires during the pipeline before the MoveFile call).

Although the CLI's argument parser rejects `->x` as a short-option token (preventing the
zero-length find through normal CLI usage), the constructor is public and can be called directly
from tests or future integrations.

**Fix:**
```csharp
public ReplaceTransform(string find, string replace)
{
    if (string.IsNullOrEmpty(find))
        throw new ArgumentException("Find string must not be empty.", nameof(find));
    Find = find;
    Replace = replace;
}
```

Also add validation in `Parse`:
```csharp
var find = operand[..separatorIndex];
if (find.Length == 0)
    throw new ArgumentException(
        "Find string must not be empty. Use format 'old->new', e.g. '.->-'.",
        nameof(operand));
```

---

### WR-04: `ValidateOutputName` does not check for Windows reserved device names

**File:** `src/FileRevamp/Output/Reporter.cs:51-63`

**Issue:** On Windows, filenames that match reserved device names (`CON`, `PRN`, `AUX`, `NUL`,
`COM1`–`COM9`, `LPT1`–`LPT9`) cannot be created as regular files. Renaming a file to `NUL.csv`
succeeds at the `File.Move` call but makes the file permanently inaccessible through normal APIs.
The current `ValidateOutputName` only checks for empty, trailing dot, and trailing space.

**Fix:**
```csharp
private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
{
    "CON", "PRN", "AUX", "NUL",
    "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
    "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
};

public static string? ValidateOutputName(string filename)
{
    if (string.IsNullOrEmpty(filename))
        return "Computed filename is empty";
    if (filename.EndsWith('.'))
        return $"Computed filename has trailing dot: '{filename}'";
    if (filename.EndsWith(' '))
        return $"Computed filename has trailing space: '{filename}'";

    // Check stem (without extension) against Windows reserved names
    var stem = Path.GetFileNameWithoutExtension(filename);
    if (WindowsReservedNames.Contains(stem))
        return $"Computed filename '{filename}' uses a Windows reserved device name";

    return null;
}
```

---

### WR-05: Dry-run mode provides no summary count of files that would be renamed

**File:** `src/FileRevamp/Commands/RenameCommand.cs:100-107`

**Issue:** In dry-run mode, after printing individual `[DRY RUN]` lines, the command prints only
`"Dry run complete — 0 files modified."`. There is no summary of how many renames WOULD occur.
A user running `--dry-run` to preview a batch operation cannot quickly determine the scope — they
must count lines manually. The `FormatDryRunComplete` method always returns a hardcoded message
ignoring the `results` list.

This is particularly misleading because `--dry-run` is described as "preview renames" but
provides less information than the live run's summary (`Renamed: N  Failed: N  Skipped: N`).

**Fix:**
```csharp
public string FormatDryRunComplete(IEnumerable<RenameResult> results)
{
    var list = results.ToList();
    var wouldRename = list.Count(r => r.Status == RenameStatus.DryRun);
    var skipped = list.Count(r => r.Status == RenameStatus.Skipped);
    var failed = list.Count(r => r.Status == RenameStatus.Failed);
    return $"Dry run complete — {wouldRename} would be renamed, {skipped} skipped, {failed} failed. 0 files modified.";
}
```

Update the call site in `RenameCommand.Execute`:
```csharp
_console.MarkupLine(Markup.Escape(reporter.FormatDryRunComplete(results)));
```

---

## Info

### IN-01: Spectre markup color is stripped from all output lines by `Markup.Escape`

**File:** `src/FileRevamp/Commands/RenameCommand.cs:97`

**Issue:** The call `_console.MarkupLine(Markup.Escape(reporter.FormatResultLine(result)))` escapes
the entire formatted line before passing it to `MarkupLine`. This prevents Spectre markup injection
from filenames (correct security behavior per T-03-01), but it also means that no colored output
is ever produced — `[red]`, `[green]`, `[yellow]` tags in `FormatResultLine` return values would
be escaped and displayed literally. Currently `FormatResultLine` doesn't include any markup tags,
so there is no regression, but the pattern prevents adding color to the output in the future.

The proper approach is to escape only the user-controlled portions (filenames) and retain markup
tags in the template:

```csharp
// In Reporter.FormatResultLine — return a markup-safe string by escaping filenames:
RenameStatus.DryRun  => $"[blue][[DRY RUN]][/] {Markup.Escape(result.OriginalName)} → {Markup.Escape(result.NewName)}",
RenameStatus.Renamed => $"[green]{Markup.Escape(result.OriginalName)} → {Markup.Escape(result.NewName)}[/]",
// Then in RenameCommand: _console.MarkupLine(reporter.FormatResultLine(result));
```

This requires `Reporter` to take a Spectre dependency, or a dedicated formatting layer. Either
way, the current approach blocks all colorized output.

---

### IN-02: `HasBareWildcard` does not check for bare `{+}` (braces containing `+` are unchecked)

**File:** `src/FileRevamp/Commands/RenameSettings.cs:50-65`

**Issue:** The bare-wildcard validator only inspects `*` and `?` characters. Users who write
`--remove "prefix+suffix"` will not get a validation warning, and `+` will be silently treated as
a literal character (correctly escaped to `\+` by `Regex.Escape`). This is not a bug in itself
since `+` has a different conventional meaning from `*` and `?` in glob syntax, but it's
inconsistent with the tool's wildcard syntax where `{+}` is a first-class token.

If the intention is to catch accidental bare use of all FileRevamp wildcard tokens, add `+` to
the check:

```csharp
if (ch == '*' || ch == '?' || ch == '+')
```

---

### IN-03: TODO comment in production code references unimplemented Phase 2 feature inline

**File:** `src/FileRevamp/Commands/RenameCommand.cs:89`

**Issue:** Line 89 contains:
```csharp
// TODO(Phase 2): stream results for large batches to avoid holding all in memory.
```

This is an acceptable tracking comment for a planned improvement, but it should be a tracked
issue rather than a code comment if the project uses an issue tracker. At minimum, the comment
should be prefixed with an issue/ticket reference so it is not silently abandoned.

---

_Reviewed: 2026-05-31_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
