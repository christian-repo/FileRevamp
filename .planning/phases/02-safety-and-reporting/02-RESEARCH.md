# Phase 2: Safety and Reporting - Research

**Researched:** 2026-06-01
**Domain:** .NET 9 / C# — collision detection, Windows auto-numbering, log file I/O, Spectre.Console.Cli help customization
**Confidence:** HIGH

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| SAFE-01 | Tool validates entire rename batch before touching any file; no file is renamed if any collision is detected | Pre-flight pass: compute all (source → destination) pairs; detect intra-batch duplicates before any `MoveFile` call |
| SAFE-02 | When computed output name already exists (on disk or within batch), auto-number using Windows convention: `file(1).csv`, `file(2).csv` | Auto-numbering algorithm: loop `i = 1..N` on `{stem}({i}){ext}` until no disk/batch collision; must run before execution phase |
| RPRT-03 | On any failure, create a plain-text log file in the target directory listing each failed filename and its reason | `File.AppendAllText` / `StreamWriter` to `rename-failures.log` in `directoryPath`; BCL-only, no logging framework |
| UX-01 | User can run `filerevamp -help` to display usage instructions and pattern examples | Spectre.Console.Cli `--help` / `-h` is auto-generated; `config.AddExample(...)` adds usage examples; `[Description]` attributes on settings properties populate option descriptions |

</phase_requirements>

---

## Summary

Phase 2 adds four behaviours to the existing .NET 9 / Spectre.Console.Cli rename tool: pre-flight collision detection (SAFE-01), Windows-style auto-numbering for collisions (SAFE-02), failure log file output (RPRT-03), and enriched help text with concrete examples (UX-01).

All four requirements are satisfied entirely using BCL types and the already-installed Spectre.Console.Cli 0.55.0 library — no new NuGet packages are needed. The heaviest design work is in `RenameOrchestrator`: the current single-pass lazy pipeline must become a two-pass design — a planning pass that computes all `(source, destination)` pairs and resolves collisions, followed by an execution pass that acts on the resolved plan.

The current conflict check (`if (fileSystem.FileExists(destPath)) → SkippedResult`) is a stub from Phase 1. Phase 2 replaces it: instead of skipping on collision, the orchestrator auto-numbers the destination so every candidate file gets renamed. SAFE-01 requires that no file is renamed if a collision cannot be resolved, but since the auto-numbering algorithm always produces a unique name (by incrementing until no disk or batch conflict exists), the "abort entire batch" semantics of SAFE-01 only trigger if the algorithm cannot find a free slot in a reasonable bound — a practically unreachable condition for normal file batches.

The failure log (RPRT-03) is a plain-text append to `rename-failures.log` in the target directory. BCL `File.AppendAllText` or a `StreamWriter` is sufficient. The log file itself must be excluded from the file discovery scan to avoid including it in the rename batch.

Help text (UX-01) is already partially satisfied by Spectre.Console.Cli's auto-generated `--help` output driven by `[Description]` attributes on `RenameSettings` properties. The remaining gap is adding concrete wildcard and replace examples via `config.AddExample(...)` calls already present in `Program.cs` — verifying those appear in `--help` output and adding a `SetApplicationVersion()` call to enable `--version`.

**Primary recommendation:** Implement a two-pass `RenameOrchestrator` — plan pass (compute + auto-number + log preview), execute pass (apply plan, log failures) — without changing the `IFileSystem` seam or any other Phase 1 types.

---

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Pre-flight collision detection | Core / RenameOrchestrator | — | Collision detection is business logic; it belongs in the orchestrator, not the CLI command |
| Auto-numbering algorithm | Core / RenameOrchestrator | — | Pure filename computation; no I/O until execution pass |
| Disk-existence check for collision | Core / IFileSystem | — | `FileExists` is already on the seam; auto-numbering queries it per candidate |
| Failure log file output | Output / FailureLogger | Core / RenameOrchestrator | Logger writes on orchestrator signal; Command wires them together |
| Help text and examples | Commands / RenameSettings + Program.cs | — | `[Description]` attributes and `config.AddExample()` drive Spectre help renderer |
| Version display | Program.cs | — | `config.SetApplicationVersion()` enables `--version` / `-v` built-in |

---

## Standard Stack

### Core (no new packages)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Spectre.Console.Cli | 0.55.0 | CLI parsing + help rendering | Already installed; `AddExample`, `SetApplicationVersion`, `[Description]` cover UX-01 |
| BCL `System.IO` (`File.AppendAllText`, `StreamWriter`) | in-box | Failure log write | Single-purpose text file; no logging framework warranted (CLAUDE.md constraint) |
| BCL `Path` | in-box | Stem/extension splitting for auto-numbering | `Path.GetFileNameWithoutExtension` / `Path.GetExtension` already used throughout |

**No new NuGet packages are introduced in Phase 2.** [VERIFIED: csproj + CLAUDE.md: "no external dependencies beyond the BCL preferred"]

### Package Legitimacy Audit

No new packages. Not applicable for Phase 2.

---

## Architecture Patterns

### System Architecture Diagram

```
CLI args
    │
    ▼
RenameSettings.Validate()          ← bare-* detection (Phase 1, already wired)
    │
    ▼
RenameCommand.Execute()
    │
    ├─► Phase 2: resolve log file path (directoryPath/rename-failures.log)
    │
    ▼
RenameOrchestrator.Plan()          ← NEW: Phase 2 planning pass
    │  for each candidate file:
    │    1. compute (source → destination) via existing transforms
    │    2. check intra-batch collision → auto-number if needed
    │    3. check disk collision (IFileSystem.FileExists) → auto-number if needed
    │    4. produce RenameProposal[] (resolved plan)
    │
    ▼
RenameOrchestrator.Execute()       ← Phase 2 execution pass
    │  for each RenameProposal:
    │    if dryRun → emit DryRunResult
    │    else → IFileSystem.MoveFile → emit Renamed/Failed
    │    if Failed → signal FailureLogger
    │
    ▼
Reporter (existing)                ← per-file output unchanged
    │
    ▼
FailureLogger                      ← NEW: writes rename-failures.log on any Failed result
```

### Recommended Project Structure

No new top-level namespace. New types fit into existing namespaces:

```
src/FileRevamp/
├── Core/
│   ├── RenameOrchestrator.cs    # MODIFIED: two-pass Plan + Execute
│   ├── RenameProposal.cs        # NEW: immutable plan record (source, resolvedDest)
│   ├── CollisionResolver.cs     # NEW: auto-numbering algorithm (testable in isolation)
│   └── ... (existing, unchanged)
├── Output/
│   ├── FailureLogger.cs         # NEW: appends failures to rename-failures.log
│   └── ... (existing, unchanged)
└── Commands/
    └── RenameCommand.cs         # MODIFIED: wire FailureLogger, excluded log from scan
```

### Pattern 1: Two-Pass Orchestrator

**What:** Separate concern of *what will happen* (plan) from *making it happen* (execute). The plan pass computes all source→destination pairs and resolves all collisions deterministically; the execute pass reads the plan and acts.

**When to use:** Any time "all-or-nothing" semantics are required before any side-effectful I/O begins. This is the industry-standard approach for atomic batch operations.

**Example (pseudocode — BCL only):**

```csharp
// Source: BCL Path API — [ASSUMED] pattern, standard .NET practice
public sealed class CollisionResolver
{
    private readonly IFileSystem _fileSystem;

    public CollisionResolver(IFileSystem fileSystem) => _fileSystem = fileSystem;

    /// <summary>
    /// Given a desired destination filename, returns a collision-free name.
    /// Checks both the in-batch already-claimed set and the real disk.
    /// Windows convention: file.csv → file(1).csv → file(2).csv → …
    /// </summary>
    public string Resolve(string directoryPath, string desiredName, HashSet<string> claimed)
    {
        var destPath = _fileSystem.Combine(directoryPath, desiredName);
        if (!claimed.Contains(desiredName) && !_fileSystem.FileExists(destPath))
            return desiredName;

        var stem = Path.GetFileNameWithoutExtension(desiredName);
        var ext  = Path.GetExtension(desiredName);

        for (var i = 1; ; i++)
        {
            var candidate = $"{stem}({i}){ext}";
            var candidatePath = _fileSystem.Combine(directoryPath, candidate);
            if (!claimed.Contains(candidate) && !_fileSystem.FileExists(candidatePath))
                return candidate;
        }
    }
}
```

**Key invariant:** `claimed` is built up as the plan pass processes each file — so if two source files both compute to `report.csv`, the second one gets `report(1).csv`, not a collision.

### Pattern 2: Failure Log — BCL append

**What:** Append each failure as a plain-text line to `rename-failures.log` in the target directory.

**When to use:** Single-purpose diagnostic file; no structured format needed; no log rotation needed.

**Example:**

```csharp
// Source: BCL System.IO — [ASSUMED] pattern, standard .NET practice
public sealed class FailureLogger
{
    private readonly string _logFilePath;

    public FailureLogger(string directoryPath)
    {
        _logFilePath = Path.Combine(directoryPath, "rename-failures.log");
    }

    public void Log(string originalName, string reason)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] FAIL {originalName}: {reason}";
        File.AppendAllText(_logFilePath, line + Environment.NewLine);
    }

    public string LogFilePath => _logFilePath;
}
```

**Important:** The log file path must be passed to `FileDiscovery` as an exclusion so `rename-failures.log` is never included as a rename candidate. The simplest approach: filter it by name from the discovered file list in `RenameCommand.Execute` before passing to the orchestrator — or pass it as an exclusion to `FileDiscovery`.

### Pattern 3: Spectre.Console.Cli Help Customization

**What:** `--help` / `-h` is automatically generated from `[Description]` attributes on `CommandSettings` properties. `config.AddExample(args[])` adds usage examples to the help output. `config.SetApplicationVersion(version)` enables `--version` / `-v`.

**When to use:** All help text should be driven by attributes and `AddExample` — never by custom `IHelpProvider` unless formatting control is required beyond what the default renderer provides.

**Example (already partially in Program.cs):**

```csharp
// Source: Spectre.Console documentation [CITED: spectreconsole.net/cli/how-to/configuring-commandapp-and-commands]
app.Configure(config =>
{
    config.SetApplicationName("filerevamp");
    config.SetApplicationVersion("1.0.0");          // enables --version / -v

    // Each AddExample call appears as a EXAMPLES block in --help output
    config.AddExample(".", "--remove", "_{*}new_{*}", "--dry-run");
    config.AddExample("./exports", "--remove", "_{*}new_{*}", "--replace", ".->-");
    config.AddExample("./reports", "--remove", "report_{*}", "--replace", " ->_");
});
```

**Current state:** `Program.cs` already has two `AddExample` calls. UX-01 requires verifying `filerevamp -help` shows at least one wildcard example and one replace example — both are already present. Phase 2 only needs to confirm this works with `-help` (single dash) alias and optionally add `SetApplicationVersion`.

**Spectre.Console.Cli `-help` alias behavior:** [VERIFIED: spectreconsole.net/cli/reference/built-in-command-behaviors] The `-h` / `--help` option is automatically available on all commands. Single-dash `-help` is NOT a standard Spectre alias — Spectre uses `--help` or `-h`. If the requirement is specifically `filerevamp -help`, this needs either: (a) a custom alias, or (b) re-reading the requirement as `--help`. The success criterion says "Running `filerevamp -help`" — this is likely shorthand meaning any help invocation, not specifically the single-dash form. Plan should verify which form the requirement means.

### Anti-Patterns to Avoid

- **Skipping on collision (Phase 1 stub):** The current `SkippedResult("Destination already exists")` code in `RenameOrchestrator` must be replaced — not extended. Phase 2 removes this skip path and routes collisions to the auto-numbering resolver instead.
- **Checking FileExists during lazy streaming:** The current orchestrator is a lazy `IEnumerable<RenameResult>`. SAFE-01 requires all-or-nothing semantics, which cannot be implemented correctly with lazy streaming — the plan pass must be eager (materialize all proposals before any execution).
- **Writing the log file inside the orchestrator:** The orchestrator produces `RenameResult[]`; the `FailureLogger` is wired by the command. Keep the orchestrator free of I/O concerns beyond `IFileSystem`.
- **Including rename-failures.log in the rename batch:** `FileDiscovery` scans `*` — if the log file already exists in the directory it will be discovered and potentially renamed. Exclude it explicitly.
- **Appending `(1)` to stem that already ends in `(N)`:** The Windows auto-numbering convention does NOT strip existing `(N)` suffixes before adding a new one. Each auto-numbering call just finds the next free slot: `file(1).csv` → `file(1)(1).csv` if `file(1).csv` already existed. This is the correct Windows Explorer behavior. [ASSUMED]

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Failure file logging | Custom logging framework (Serilog, NLog, etc.) | `File.AppendAllText` / `StreamWriter` BCL | CLAUDE.md constraint: "no external dependencies beyond BCL preferred"; single-line append is all that's needed |
| Path stem/extension splitting | Manual string manipulation | `Path.GetFileNameWithoutExtension` + `Path.GetExtension` | BCL handles edge cases (dotfiles like `.gitignore`, multiple dots like `file.min.js`) |
| In-batch collision tracking | External data structure | `HashSet<string>(StringComparer.OrdinalIgnoreCase)` | O(1) lookup, case-insensitive (NTFS is case-insensitive on Windows) |
| Help text formatting | Custom `IHelpProvider` | `[Description]` attributes + `config.AddExample()` | Spectre renders them automatically; custom provider adds complexity with no gain |

**Key insight:** All Phase 2 work is algorithmic logic + BCL I/O. There is no problem in this phase that requires a new library.

---

## Common Pitfalls

### Pitfall 1: Lazy streaming breaks SAFE-01 (all-or-nothing semantics)

**What goes wrong:** The current orchestrator returns `IEnumerable<RenameResult>` lazily. If you add collision checking inside the iterator, you cannot detect ALL collisions before the first `MoveFile` call — because callers who don't fully enumerate the sequence will miss later failures.

**Why it happens:** Lazy iterators are evaluated on-demand. `RenameCommand` does `.ToList()` which forces evaluation, but the current architecture couples "plan" and "execute" in a single pass — meaning by the time a collision is detected mid-stream, earlier files may already be renamed.

**How to avoid:** Split into a `Plan()` pass (eager, returns `RenameProposal[]`) and an `Execute()` pass (takes the proposal array). `Plan()` resolves all collisions and validates; `Execute()` only runs after `Plan()` completes successfully. The existing `RenameCommand.Execute` method already calls `.ToList()` — the split can be implemented by refactoring the orchestrator without changing the command's contract.

**Warning signs:** Any implementation where `MoveFile` can be called before all source→destination pairs have been computed.

### Pitfall 2: Intra-batch collision NOT detected by FileExists

**What goes wrong:** `_fileSystem.FileExists(destPath)` only checks what exists on disk at the moment of the check. If two files in the same batch both compute to `report.csv`, the first one's destination does not exist on disk yet — so `FileExists` returns false for both, and both proceed. The second `MoveFile` will fail at runtime (destination already moved to by the first), producing a partial rename and a failure.

**Why it happens:** The plan pass must track a running `HashSet<string>` of destinations already claimed by earlier proposals in the same batch. The disk check and the in-batch check must both be performed.

**How to avoid:** In `CollisionResolver.Resolve(...)`, pass a `claimed` set (all destinations already assigned in this batch) and check it alongside `FileExists`. Add each resolved destination to `claimed` immediately after assignment.

**Warning signs:** Tests that use `MockFileSystem` with two files computing to the same name, without a `claimed` set tracking — the collision will not be detected.

### Pitfall 3: Log file included in the rename batch

**What goes wrong:** `rename-failures.log` lives in the same directory as the files being renamed. On the second run (when the log already exists), `FileDiscovery.GetFiles("*")` returns it as a candidate. A remove pattern like `_{*}` could transform `rename-failures.log` → `rename-failures.log` (no match → skip) or, with a broad pattern, produce an unintended rename.

**Why it happens:** `FileDiscovery` has no exclusion list. The log file path is a side-effect of running the tool.

**How to avoid:** In `RenameCommand.Execute`, after `FileDiscovery.GetFiles(...)`, filter out any path whose filename is `rename-failures.log` before passing the list to the orchestrator. Do NOT add exclusion logic to `FileDiscovery` itself — the exclusion is a command-level concern, not a discovery concern.

**Warning signs:** A test that creates `rename-failures.log` in the temp dir and runs the tool — the log file should never appear in the output as a rename candidate.

### Pitfall 4: Auto-numbering produces a name that was already claimed

**What goes wrong:** The auto-numbering loop checks disk AND the batch's claimed set each iteration. If you forget to check the claimed set, two files in the same batch can be assigned `file(1).csv` independently.

**Why it happens:** The claimed set must be passed by reference to the resolver and updated atomically (check → if free → add to claimed, return). If the resolver returns without updating claimed, the next call for the same desired name will see the same free slot and assign it twice.

**How to avoid:** The resolver must add the resolved name to `claimed` before returning. The caller (plan pass) must pass the same `claimed` instance for all files in the batch.

### Pitfall 5: `-help` vs `--help` in UX-01

**What goes wrong:** Spectre.Console.Cli auto-generates `--help` and `-h`. Single-dash `-help` is NOT a standard alias. Invoking `filerevamp -help` will fail with an unrecognized option error in Spectre.

**Why it happens:** The requirement says `filerevamp -help` but the standard CLI convention and Spectre's default is `--help`. This is a documentation/wording ambiguity.

**How to avoid:** The plan should implement `--help` behavior (already works) and note that `-help` is a single-dash form — if the requirement literally means `-help` (not `--help`), a custom alias or `PropagateExceptions`/help intercept is needed. Recommend treating "filerevamp -help" in the requirement as shorthand for "filerevamp --help" unless the user clarifies otherwise.

### Pitfall 6: FailureLogger creates file even when there are no failures

**What goes wrong:** If `FailureLogger` opens/creates the file at construction time (not on first write), an empty `rename-failures.log` appears in the directory on every run, even successful ones. This is unexpected and pollutes the target directory.

**Why it happens:** Eager file creation in the constructor.

**How to avoid:** `FailureLogger` must be lazy — only create/append to the file when `Log(...)` is actually called. Use `File.AppendAllText(...)` (creates the file on first call if it doesn't exist; does not create eagerly) or create a `StreamWriter` in the `Log()` method, not the constructor.

---

## Code Examples

### Auto-Numbering Algorithm

```csharp
// Source: BCL Path API, standard Windows Explorer convention [ASSUMED]
// This is the canonical implementation — no external library
public string ResolveCollision(string directoryPath, string desiredName, HashSet<string> batchClaimed)
{
    // Fast path: desired name is free (not on disk, not claimed in batch)
    var destPath = _fileSystem.Combine(directoryPath, desiredName);
    if (!batchClaimed.Contains(desiredName, StringComparer.OrdinalIgnoreCase)
        && !_fileSystem.FileExists(destPath))
    {
        batchClaimed.Add(desiredName);
        return desiredName;
    }

    // Slow path: find the first free slot file(1), file(2), ...
    var stem = Path.GetFileNameWithoutExtension(desiredName);
    var ext  = Path.GetExtension(desiredName);  // includes leading dot, or "" if none

    for (var i = 1; ; i++)
    {
        var candidate = $"{stem}({i}){ext}";
        var candidatePath = _fileSystem.Combine(directoryPath, candidate);
        if (!batchClaimed.Contains(candidate, StringComparer.OrdinalIgnoreCase)
            && !_fileSystem.FileExists(candidatePath))
        {
            batchClaimed.Add(candidate);
            return candidate;
        }
    }
}
```

### Failure Log Write (BCL only)

```csharp
// Source: BCL System.IO [ASSUMED] — standard .NET append pattern
public void LogFailure(string originalName, string reason)
{
    var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] FAIL {originalName}: {reason}";
    File.AppendAllText(_logFilePath, line + Environment.NewLine);
}
```

### Excluding Log File from Discovery

```csharp
// In RenameCommand.Execute — filter after discovery, before orchestrator
// [ASSUMED] — clean separation of concerns
const string LogFileName = "rename-failures.log";
var filePaths = new FileDiscovery(fileSystem)
    .GetFiles(directoryPath, globPattern)
    .Where(p => !string.Equals(
        fileSystem.GetFileName(p), LogFileName,
        StringComparison.OrdinalIgnoreCase))
    .ToList();
```

### Two-Pass Orchestrator Sketch

```csharp
// [ASSUMED] — illustrative interface redesign
// Plan pass: returns resolved proposals, no file I/O
public IReadOnlyList<RenameProposal> Plan(
    IReadOnlyList<string> filePaths,
    WildcardPatternMatcher patternMatcher,
    IReadOnlyList<ReplaceTransform> replaceTransforms,
    string directoryPath)
{
    var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var proposals = new List<RenameProposal>();
    foreach (var filePath in filePaths)
    {
        // ... compute newName via existing removes + replaces ...
        // ... resolve collision via CollisionResolver ...
        proposals.Add(new RenameProposal(filename, resolvedName, ...));
    }
    return proposals;
}

// Execute pass: acts on the plan
public IReadOnlyList<RenameResult> Execute(
    IReadOnlyList<RenameProposal> plan,
    string directoryPath,
    bool dryRun)
{ ... }
```

---

## Project Constraints (from CLAUDE.md)

| Directive | Implication for Phase 2 |
|-----------|------------------------|
| No external dependencies beyond BCL preferred | No Serilog, NLog, or other logging packages; use `File.AppendAllText` for failure log |
| Windows-first platform | Path comparisons use `OrdinalIgnoreCase`; `Path.GetInvalidFileNameChars()` already called |
| Single-project structure | No new `.csproj` files; new types in `Core/` and `Output/` namespaces within the existing project |
| xUnit + FluentAssertions + Spectre.Console.Cli.Testing for tests | Maintain existing test patterns; new tests follow same TDD/RED-GREEN structure |
| Git workflow: feature/phase-{N}-{slug} branch | Current branch: `plan/phase-01-core-rename-pipeline` — new phase will use `feature/phase-02-safety-and-reporting` |
| Phase complete: gh pr create --base main --fill | Phase 2 ends with a PR, not a direct push |

---

## State of the Art

| Old Approach (Phase 1 stub) | Phase 2 Approach | Impact |
|-----------------------------|------------------|--------|
| `SkippedResult("Destination already exists")` | Auto-number collision → `file(1).csv`; SAFE-01 all-or-nothing | Batch never partially renames; collisions resolved, not skipped |
| Single-pass lazy `IEnumerable<RenameResult>` | Two-pass: `Plan()` (eager) → `Execute()` | Enables pre-flight validation; SAFE-01 satisfied |
| No log file | `rename-failures.log` in target directory | RPRT-03: persistent failure record |
| Help with two examples (already in `Program.cs`) | Same examples verified; `SetApplicationVersion` added | UX-01: help confirmed complete |

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | Windows auto-numbering convention is `file(1).csv` (no space before paren) | Standard Stack, Code Examples | If Windows Explorer uses `file (1).csv` (with space), the output format differs from what users expect. Verify manually: copy a file over itself in File Explorer and observe the suffix. |
| A2 | `filerevamp -help` in UX-01 is shorthand for `--help` or `-h`, not specifically the single-dash form | Architecture Patterns Pitfall 5 | If literally `-help` is required, Spectre does not support it by default; a custom alias or argument intercept is needed |
| A3 | Auto-numbering does not strip existing `(N)` suffixes before incrementing | Anti-Patterns section | If the tool is expected to produce `file(2).csv` instead of `file(1)(1).csv` for a double-collision, the resolver needs a regex to detect and strip existing `(N)` patterns first |
| A4 | `FailureLogger` should use `File.AppendAllText` (lazy creation) | Code Examples | If a separate log file per-run is required (not append), a timestamped filename or file-create approach is needed |
| A5 | Excluding `rename-failures.log` by filename constant is sufficient | Common Pitfalls | If the user has a legitimately named file `rename-failures.log` they want to rename, this exclusion silently skips it — they cannot rename that specific file with this tool |

**If this table is empty:** All claims were verified or cited — no user confirmation needed.

---

## Open Questions (RESOLVED)

1. **Windows auto-numbering format: `file(1).csv` vs `file (1).csv` (space before paren)?**
   - What we know: Roadmap/REQUIREMENTS.md explicitly specifies `file(1).csv` (no space)
   - What's unclear: Whether this matches Windows Explorer's actual behavior (which uses a space)
   - Recommendation: Follow the requirement spec exactly (`file(1).csv`); if users complain the format differs from Explorer, update in Phase 3

2. **`-help` vs `--help` for UX-01**
   - What we know: Spectre auto-generates `--help` and `-h`. Single-dash `-help` is not a default alias.
   - What's unclear: Whether the requirement literally means `-help` or is just using it loosely to mean "the help flag"
   - Recommendation: Implement with `--help` (already works), note in plan that `-help` is not a standard Spectre alias; if needed add a custom alias

3. **Log file append vs create-per-run**
   - What we know: RPRT-03 says "creates a log file" — singular. `File.AppendAllText` both creates and appends.
   - What's unclear: Whether repeated runs should overwrite or append to the log
   - Recommendation: Default to append; this is safer (preserves history). If the user wants per-run logs, a timestamp-based filename is better left to Phase 3.

---

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET SDK | All tasks | ✓ | 10.0.102 (RollForward=Major covers 9→10) | — |
| Spectre.Console.Cli 0.55.0 | UX-01, CLI | ✓ | 0.55.0 (in csproj) | — |
| BCL System.IO | RPRT-03 | ✓ | in-box | — |
| BCL System.Text.RegularExpressions | Core engine | ✓ | in-box | — |

No missing dependencies. All required capabilities are available in the current project.

---

## Validation Architecture

nyquist_validation is `false` in `.planning/config.json`. This section is skipped.

---

## Security Domain

`security_enforcement: true`, `security_asvs_level: 1` in config.

### New Trust Boundaries Introduced in Phase 2

| Boundary | Description |
|----------|-------------|
| FailureLogger → disk (write) | Writes to `rename-failures.log` in the user-supplied directory; path is derived from `directoryPath` already validated by `Path.GetFullPath` at command entry (T-01-02) |
| CollisionResolver → IFileSystem.FileExists (read) | Queries disk existence in a loop; bounded by the number of files in the batch; no path construction outside `directoryPath` |

### Applicable ASVS Categories (Level 1)

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | No | Developer CLI tool; no user auth |
| V3 Session Management | No | Stateless per-invocation |
| V4 Access Control | No | OS enforces; tool operates as the invoking user |
| V5 Input Validation | Yes | `Reporter.ValidateOutputName` (Phase 1); `Path.GetInvalidFileNameChars` (Phase 1); path traversal check (Phase 1) — all inherited; no new input surfaces in Phase 2 |
| V6 Cryptography | No | No crypto operations |

### Phase 2 Threat Register

| Threat ID | Category | Component | Disposition | Mitigation |
|-----------|----------|-----------|-------------|------------|
| T-04-01 | Tampering | FailureLogger — log file written to `directoryPath` | mitigate | `directoryPath` is already resolved via `Path.GetFullPath` (T-01-02); log file path is `Path.Combine(directoryPath, "rename-failures.log")` — no user-controlled path segments in the log filename |
| T-04-02 | Denial of Service | CollisionResolver infinite loop if disk is full or all slots taken | accept | The loop terminates when it finds a free slot; if the disk is truly full or 10^9 files named `file(N)` exist, the OS will reject the rename anyway — no new surface |
| T-04-03 | Information Disclosure | `rename-failures.log` reveals filenames and failure reasons | accept | Consistent with RPRT-03 requirement; failures are about the user's own files; developer tool, no multi-user surface (same rationale as T-01-04, AR-02) |
| T-04-04 | Tampering | Auto-numbering loop queries FileExists in a tight loop — could traverse unexpected files | accept | `_fileSystem.Combine(directoryPath, candidate)` — all path queries are within `directoryPath`; same bound as existing FileExists calls |

---

## Sources

### Primary (HIGH confidence)
- [Spectre.Console.Cli — Built-in Command Behaviors](https://spectreconsole.net/cli/reference/built-in-command-behaviors) — `--help`, `--version` built-in; `-h` alias confirmed
- [Spectre.Console.Cli — Configuring CommandApp and Commands](https://spectreconsole.net/cli/how-to/configuring-commandapp-and-commands) — `AddExample`, `SetApplicationVersion`, `WithDescription` pattern
- [Spectre.Console.Cli — Customizing Help Text](https://spectreconsole.net/cli/how-to/customizing-help-text-and-usage) — `HelpProviderStyles`, `IHelpProvider`, `SetHelpProvider`
- Phase 1 codebase: `src/FileRevamp/Core/RenameOrchestrator.cs`, `IFileSystem.cs`, `MockFileSystem.cs`, `Reporter.cs`, `RenameResult.cs` — all read directly

### Secondary (MEDIUM confidence)
- REQUIREMENTS.md `SAFE-02` wording: `file(1).csv` format specified explicitly by the project
- ROADMAP.md Phase 2 success criteria: all four success criteria read directly
- STATE.md decisions: "Auto-number on conflict assigned to Phase 2"

### Tertiary (LOW confidence)
- Windows File Explorer auto-numbering behavior observation — common knowledge; not verified programmatically in this session [ASSUMED A1]

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no new packages; BCL types confirmed; existing Spectre version confirmed from csproj
- Architecture: HIGH — two-pass design is a well-established pattern; all types to create are minimal and clearly scoped
- Auto-numbering algorithm: HIGH for the loop structure; MEDIUM for exact format (`file(1)` vs `file (1)`) — spec dictates the format regardless
- Pitfalls: HIGH — pitfalls 1–3 derived directly from the existing code (lazy iterator, FileExists-only check, log file discovery); pitfalls 4–6 derived from standard implementation concerns

**Research date:** 2026-06-01
**Valid until:** 2026-07-01 (stable stack; Spectre 0.55.0 pinned)
