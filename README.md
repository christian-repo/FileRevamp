# FileRevamp

A .NET 9 command-line tool for batch-renaming files using pattern-based transforms. Pass remove or replace operations, preview exactly what will change with `--dry-run`, then execute — no silent surprises.

## Features

- **Wildcard remove** — strip segments from filenames using `{*}`, `{+}`, `{?}` tokens
- **Literal replace** — substitute any substring (`old->new`)
- **Dry-run preview** — see every rename before it happens, no files touched
- **Collision safety** — auto-numbers conflicting output names (`file(1).csv`) using a pre-flight two-pass plan
- **Failure log** — writes `rename-failures.log` to the target directory on any error (only created when failures occur)
- **Glob targeting** — target a directory or a glob pattern (`*.csv`, `exports/*.txt`)
- **Windows-first** — handles Unicode filenames, long paths (near MAX_PATH), and case-insensitive matching

---

## Installation

### As a dotnet global tool

```bash
dotnet tool install -g FileRevamp --add-source ./nupkg
```

After install, `filerevamp` is available from any shell prompt.

### Self-contained single binary (no .NET required on target machine)

```bash
dotnet publish src/FileRevamp/FileRevamp.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o ./dist
```

Copy `dist/FileRevamp.exe` anywhere. No .NET installation needed on the target machine.

Other runtime targets: `win-x86`, `linux-x64`, `osx-x64`, `osx-arm64`.

### Build from source

```bash
dotnet restore
dotnet build
dotnet run --project src/FileRevamp -- <path> [options]
```

---

## Usage

```
filerevamp <path> [--remove <pattern>]... [--replace <old->new>]... [--dry-run]
```

| Argument / Option | Description |
|---|---|
| `<path>` | Directory path or glob pattern (e.g. `C:\exports` or `C:\exports\*.csv`) |
| `--remove <pattern>` | Wildcard pattern to remove from filenames. Repeatable. |
| `--replace <old->new>` | Literal replace in the form `old->new`. Find is case-sensitive. Repeatable. |
| `--dry-run` / `-n` | Preview renames without modifying any files. |

**Operation order is fixed:** all `--remove` patterns apply first, then all `--replace` operations.

---

## Wildcard Syntax

`--remove` uses a simplified wildcard syntax. All non-token characters are treated as **literals** (dots, parentheses, hyphens are safe).

| Token | Matches | Regex equivalent |
|---|---|---|
| `{*}` | Zero or more characters | `.*` |
| `{+}` | One or more characters | `.+` |
| `{?}` | Zero or one character | `.?` |

Matching is **case-insensitive**.

> Use `{*}` and `{+}` instead of bare `*` and `+`. The tool will error on bare wildcards with a hint.

---

## Examples

### Remove a segment from filenames

```bash
# Before: report_new_2024.csv  →  After: report_2024.csv
filerevamp C:\exports --remove _new

# Before: export_draft_final.csv  →  After: export_final.csv  (wildcard strips middle segment)
filerevamp C:\exports --remove export_{*}draft_
```

### Remove literal dots and parentheses

```bash
# Before: report.new.(2024).csv  →  After: report.new.csv
filerevamp C:\exports --remove ".(2024)"
```

### Replace substrings

```bash
# Before: my.report.final.csv  →  After: my-report-final.csv
filerevamp C:\exports --replace .->-

# Before: PREFIX_report.csv  →  After: report.csv
filerevamp C:\exports --replace PREFIX_->
```

### Combine remove and replace

```bash
# Remove _draft_ segment, then replace spaces with underscores
filerevamp C:\exports --remove _draft_ --replace " "->_
```

### Target a glob pattern

```bash
# Only process .csv files in the directory
filerevamp "C:\exports\*.csv" --remove _backup
```

### Dry-run preview

```bash
filerevamp C:\exports --remove _new --dry-run
# [DRY RUN] report_new.csv → report.csv
# [DRY RUN] data_new.csv   → data.csv
# Would rename: 2  |  Skipped: 0
```

### Collision handling

When two source files resolve to the same output name, the second gets auto-numbered:

```bash
filerevamp C:\exports --replace prefix_-> --replace suffix_->
# prefix_report.csv → report.csv
# suffix_report.csv → report(1).csv
```

---

## Output

Each processed file prints one line:

```
report_new.csv → report.csv          (renamed)
data.csv → data.csv  [skipped]       (no change produced by the transform)
locked.csv → locked.csv  [FAIL]      (error — see rename-failures.log)
```

On completion:

```
Renamed: 5  |  Failed: 1  |  Skipped: 2
```

When any rename fails, `rename-failures.log` is created in the target directory:

```
[2026-06-03 14:22:01Z] FAIL locked.csv: Access to the path is denied.
```

---

## Architecture

FileRevamp is a single .NET project with namespace-based separation.

```
src/FileRevamp/
├── Commands/
│   ├── RenameCommand.cs          # Spectre.Console.Cli Command<T> — thin wiring only
│   └── RenameSettings.cs         # CLI arguments and options with validation
├── Core/
│   ├── IFileSystem.cs            # Seam for testability
│   ├── FileSystem.cs             # Production implementation — calls System.IO
│   ├── DryRunFileSystem.cs       # No-op MoveFile; reads are real
│   ├── FileDiscovery.cs          # Directory + glob enumeration via FileSystemGlobbing
│   ├── WildcardCompiler.cs       # Translates {*}/{+}/{?} tokens to compiled Regex
│   ├── WildcardPatternMatcher.cs # Applies remove patterns to a filename stem
│   ├── ReplaceTransform.cs       # Parses and applies old->new literal replacement
│   ├── RenameOrchestrator.cs     # Two-pass Plan() + Execute()
│   ├── RenameProposal.cs         # Immutable record: source path + resolved destination
│   └── CollisionResolver.cs      # Auto-numbers destinations; source-aware
├── Output/
│   ├── RenameResult.cs           # Result record: status (Renamed/Skipped/Failed) + names
│   ├── Reporter.cs               # Formats per-file lines and summary/dry-run footer
│   └── FailureLogger.cs          # Lazy-append rename-failures.log; UTC timestamps
├── Infrastructure/
│   └── TypeRegistrar.cs          # Spectre.Console.Cli DI registrar
└── Program.cs                    # Entry point — registers command, sets version
```

### Two-Pass Rename Pipeline

The rename pipeline separates planning from execution to guarantee no file is touched before all collisions are resolved:

1. **Plan** — `RenameOrchestrator.Plan()` computes every `source → destination` pair, resolves collisions via `CollisionResolver`, and returns `RenameProposal[]` plus any early-exit results (skipped files, validation failures).
2. **Execute** — `RenameOrchestrator.Execute()` calls `IFileSystem.MoveFile` for each proposal. In dry-run mode, `DryRunFileSystem.MoveFile` is a no-op and nothing is written to disk.

### Collision Resolution

`CollisionResolver` is source-aware: it receives the set of all source filenames in the batch and skips the disk-exists check for those names, since they will vacate during Execute. This prevents false-positive auto-numbering when a computed destination matches a source file that is also being renamed in the same batch.

### Wildcard Compilation

`WildcardCompiler.ToRegex` applies a strict four-step conversion:

1. `Regex.Escape(pattern)` — escapes all regex metacharacters first (dots, parens, brackets become literals)
2. Replace the now-escaped brace tokens (`\{\*}` → `(.*?)`) with non-greedy quantifiers
3. Optionally wrap in `^...$` anchors
4. Compile with `RegexOptions.IgnoreCase | RegexOptions.Compiled`

Step 1 before Step 2 is critical: it ensures literal `.`, `(`, `)` in patterns are never interpreted as regex metacharacters.

---

## Tech Stack

| Component | Technology |
|---|---|
| Runtime | .NET 9 (`net9.0`) |
| CLI framework | Spectre.Console.Cli 0.55.0 |
| File globbing | Microsoft.Extensions.FileSystemGlobbing 9.x |
| Pattern matching | BCL `System.Text.RegularExpressions.Regex` |
| Test runner | xUnit 2.9 |
| Assertions | FluentAssertions 7.x |
| CLI test harness | Spectre.Console.Cli.Testing (`CommandAppTester`) |

---

## Development

```bash
# Restore and build
dotnet restore
dotnet build

# Run all tests
dotnet test

# Run a specific test
dotnet test --filter "FullyQualifiedName~CollisionResolverTests"

# Pack as dotnet tool
dotnet pack src/FileRevamp/FileRevamp.csproj -c Release

# Install locally from nupkg
dotnet tool install -g FileRevamp --add-source ./src/FileRevamp/nupkg
```

---

## License

MIT
