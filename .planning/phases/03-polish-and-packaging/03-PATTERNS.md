# Phase 3: Polish and Packaging - Pattern Map

**Mapped:** 2026-06-03
**Files analyzed:** 5
**Analogs found:** 5 / 5

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|---|---|---|---|---|
| `tests/FileRevamp.Tests/Commands/EdgeCaseIntegrationTests.cs` | test | request-response | `tests/FileRevamp.Tests/Commands/RenameCommandTests.cs` | exact |
| `tests/FileRevamp.Tests/Fakes/MockFileSystem.cs` | utility | CRUD | `src/FileRevamp/Core/MockFileSystem.cs` | exact (move) |
| `src/FileRevamp/Commands/RenameCommand.cs` | command | request-response | `src/FileRevamp/Commands/RenameCommand.cs` | self (modify) |
| `src/FileRevamp/FileRevamp.csproj` | config | — | `src/FileRevamp/FileRevamp.csproj` | self (modify) |
| `src/FileRevamp/Program.cs` | config | — | `src/FileRevamp/Program.cs` | self (modify) |

---

## Pattern Assignments

### `tests/FileRevamp.Tests/Commands/EdgeCaseIntegrationTests.cs` (test, request-response)

**Analog:** `tests/FileRevamp.Tests/Commands/RenameCommandTests.cs`

This is the primary deliverable for Phase 3. All six edge-case tests (D-02 items 1–6) go here as a new test class alongside the existing `RenameCommandTests`. Copy the class structure wholesale.

**Imports pattern** (lines 1–6):
```csharp
using FileRevamp.Commands;
using FluentAssertions;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Testing;

namespace FileRevamp.Tests.Commands;
```

**Class scaffold pattern** (lines 14–62):
```csharp
public sealed class RenameCommandTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    private static CommandAppTester CreateTester()
    {
        var tester = new CommandAppTester();
        var registrar = new FakeTypeRegistrar();
        registrar.RegisterInstance(typeof(IAnsiConsole), tester.Console);
        registrar.RegisterInstance(typeof(RenameCommand), new RenameCommand(tester.Console));
        tester.Registrar = registrar;
        tester.SetDefaultCommand<RenameCommand>("Rename files in a directory");
        tester.Configure(config =>
        {
            config.SetApplicationName("filerevamp");
        });
        return tester;
    }

    private string CreateTempDir(params string[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"filerevamp_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        foreach (var file in files)
            File.WriteAllText(Path.Combine(dir, file), string.Empty);
        return dir;
    }
```

**Individual test pattern** (lines 64–131 — live rename test):
```csharp
[Fact]
public void LiveRun_RenamesFileOnDisk_OutputContainsRenameAndSummary()
{
    var tempDir = CreateTempDir("report_new_data.csv");
    var tester = CreateTester();

    var result = tester.Run(tempDir, "--remove", "_new");

    result.ExitCode.Should().Be(0);
    result.Output.Should().Contain("report_new_data.csv");
    result.Output.Should().Contain("report_data.csv");
    result.Output.Should().Contain("Renamed: 1");
    result.Output.Should().Contain("Failed: 0");

    File.Exists(Path.Combine(tempDir, "report_data.csv")).Should().BeTrue();
    File.Exists(Path.Combine(tempDir, "report_new_data.csv")).Should().BeFalse();
}
```

**Dry-run assertions pattern** (lines 78–95):
```csharp
result.ExitCode.Should().Be(0);
result.Output.Should().Contain("[DRY RUN]");
result.Output.Should().Contain("Dry run complete");
result.Output.Should().Contain("0 files modified");
result.Output.Should().Contain("would be renamed");

File.Exists(Path.Combine(tempDir, "report_new_data.csv")).Should().BeTrue();
File.Exists(Path.Combine(tempDir, "report_data.csv")).Should().BeFalse();
```

**Collision dry-run pattern** (lines 148–163):
```csharp
var result = tester.Run(
    tempDir, "--replace", "prefix_->", "--replace", "suffix_->", "--dry-run");

result.ExitCode.Should().Be(0);
result.Output.Should().Contain("report.csv", because: "first file gets the base resolved name");
result.Output.Should().Contain("report(1).csv", because: "second colliding file is auto-numbered");
```

**Failure/log assertions pattern** (lines 165–178):
```csharp
result.ExitCode.Should().Be(1, because: "at least one file failed");
var logPath = Path.Combine(tempDir, "rename-failures.log");
File.Exists(logPath).Should().BeTrue(because: "failure log must be created when a rename fails (RPRT-03)");
File.ReadAllText(logPath).Should().Contain("report.csv", because: "log must name the failed file");
```

**Key implementation notes for each edge case:**

1. **Literal dots/parens** (`report.new.(2024).csv` with `--remove` `.(2024)`):
   - `WildcardCompiler.ToRegex` already Regex.Escapes the literal before substituting brace tokens (lines 41–52 of `WildcardCompiler.cs`). Test that the rename **succeeds** and produces `report.new.csv`. Assert `ExitCode == 0`, `Renamed: 1`, file exists at new name.

2. **Batch collisions** (two files resolving to same output name, live run):
   - Extends the dry-run collision test (lines 148–163). Run **without** `--dry-run`. Assert `ExitCode == 0`, `Renamed: 2` (no skips), both output files exist on disk.

3. **Log file exclusion** (`rename-failures.log` pre-present, live run):
   - Already covered by `LiveRun_LogFileInDirectory_IsExcludedFromBatch` in `RenameCommandTests`. For Phase 3 add a variant that uses a wildcard that would match `rename-failures.log` (e.g. `--remove` `rename-`) to confirm the filter is not pattern-specific.

4. **Empty directory** (no files):
   - `CreateTempDir()` with no file args → `tester.Run(tempDir, "--remove", "_new")`. Assert `ExitCode == 0`, output contains `Renamed: 0`, no crash.

5. **Unicode filenames** (`café_new.csv` → `café.csv`):
   - `CreateTempDir("café_new.csv")` → `tester.Run(tempDir, "--remove", "_new")`. Assert `ExitCode == 0`, `File.Exists(Path.Combine(tempDir, "café.csv"))`.

6. **Long filenames** (near MAX_PATH ~255 chars):
   - Construct a filename of 245 characters (safe margin) plus extension. Assert `ExitCode == 0`, renamed file exists. Use `new string('a', 240) + "_new.csv"` → remove `_new`.

---

### `tests/FileRevamp.Tests/Fakes/MockFileSystem.cs` (utility, CRUD — moved from src/)

**Analog:** `src/FileRevamp/Core/MockFileSystem.cs` (exact move per WR-04 from Phase 2 review)

**Source file** `src/FileRevamp/Core/MockFileSystem.cs` (full, 53 lines):
```csharp
namespace FileRevamp.Core;

public sealed class MockFileSystem : IFileSystem
{
    private readonly Dictionary<string, bool> _files =
        new(StringComparer.OrdinalIgnoreCase);

    public int MoveCallCount { get; private set; }

    public MockFileSystem(IEnumerable<string> initialFiles)
    {
        foreach (var path in initialFiles)
            _files[Normalize(path)] = true;
    }

    public IEnumerable<string> GetFiles(string directoryPath, string searchPattern) { ... }
    public void MoveFile(string sourcePath, string destPath) { ... }
    public bool FileExists(string path) => _files.ContainsKey(Normalize(path));
    public string GetFileName(string path) => Path.GetFileName(path) ?? path;
    public string Combine(string dir, string filename) =>
        Normalize(dir).TrimEnd('/') + "/" + filename;
    private static string Normalize(string path) => path.Replace('\\', '/');
}
```

**Target namespace** for moved file: `FileRevamp.Tests.Fakes` (directory `tests/FileRevamp.Tests/Fakes/`).

**Using directive to add** in all test files that reference `MockFileSystem`:
```csharp
using FileRevamp.Tests.Fakes;
```

Files requiring the updated using directive after the move:
- `tests/FileRevamp.Tests/Core/CollisionResolverTests.cs`
- `tests/FileRevamp.Tests/Core/RenameOrchestratorTests.cs`

---

### `src/FileRevamp/Commands/RenameCommand.cs` (command — IFileSystem injection)

**Analog:** Self — extend the existing optional-injection pattern already established for `IAnsiConsole`.

**Existing IAnsiConsole injection pattern** (lines 17–24 of `RenameCommand.cs`):
```csharp
private readonly IAnsiConsole _console;

public RenameCommand(IAnsiConsole? console = null)
{
    _console = console ?? AnsiConsole.Console;
}
```

**Pattern to replicate for IFileSystem** — add a second optional constructor parameter with the same null-default fallback:
```csharp
private readonly IAnsiConsole _console;
private readonly IFileSystem? _injectedFileSystem;

public RenameCommand(IAnsiConsole? console = null, IFileSystem? fileSystem = null)
{
    _console = console ?? AnsiConsole.Console;
    _injectedFileSystem = fileSystem;
}
```

Then replace the hardcoded `new FileSystem()` / `new DryRunFileSystem()` selection (lines 59–62):
```csharp
// Choose the file system implementation based on dry-run flag.
// _injectedFileSystem is non-null only in tests (in-memory); in production it is always null.
IFileSystem fileSystem = _injectedFileSystem
    ?? (settings.DryRun ? new DryRunFileSystem() : new FileSystem());
```

**Note:** `DryRunFileSystem` is only used when `_injectedFileSystem` is null. When a test injects `MockFileSystem`, dryRun behavior is signaled to the orchestrator via the `dryRun: bool` argument to `Execute()`, not through the file system type. The `MockFileSystem` does not call `MoveFile` in dry-run because the orchestrator passes `dryRun: true` — the injected `MockFileSystem` is only used for `FileExists` and `GetFiles` lookups.

**Tester registration pattern** (from `RenameCommandTests.cs` lines 38–42) extended for IFileSystem:
```csharp
var mockFs = new MockFileSystem(new[] { "/path/to/file.csv" });
registrar.RegisterInstance(typeof(IAnsiConsole), tester.Console);
registrar.RegisterInstance(typeof(RenameCommand), new RenameCommand(tester.Console, mockFs));
```

---

### `src/FileRevamp/FileRevamp.csproj` (config — package metadata)

**Analog:** Self — extend the existing `<PropertyGroup>` block that already has `PackAsTool=true`.

**Existing packaging scaffold** (lines 10–13):
```xml
<!-- dotnet tool packaging -->
<PackAsTool>true</PackAsTool>
<ToolCommandName>filerevamp</ToolCommandName>
<PackageOutputPath>./nupkg</PackageOutputPath>
```

**Properties to add** in the same `<PropertyGroup>`:
```xml
<PackageId>FileRevamp</PackageId>
<Version>1.0.0</Version>
<Authors>FileRevamp Contributors</Authors>
<Description>Batch file renaming tool with wildcard and regex pattern support.</Description>
<PackageLicenseExpression>MIT</PackageLicenseExpression>
<RepositoryUrl>https://github.com/CodexArtifex19/FileRevamp</RepositoryUrl>
<PackageReadmeFile>README.md</PackageReadmeFile>
```

Do NOT add `<PackageReadmeFile>` if `README.md` does not exist at the repo root at time of implementation — it causes `dotnet pack` to fail if the file is missing.

---

### `src/FileRevamp/Program.cs` (config — version string)

**Analog:** Self — IN-01 from Phase 2 review: `SetApplicationVersion` is absent from `Program.cs` despite being described in context.

**Existing configure block** (lines 4–12):
```csharp
var app = new CommandApp<RenameCommand>();
app.Configure(config =>
{
    config.SetApplicationName("filerevamp");
    config.SetApplicationVersion("1.0.0");
    config.AddExample(".", "--remove", "_{*}new_{*}", "--dry-run");
    config.AddExample("./exports", "--remove", "_{*}new_{*}", "--replace", ".->-");
});
```

**Fix to apply** — replace the hardcoded string with assembly-derived version per IN-01:
```csharp
config.SetApplicationVersion(
    typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0");
```

---

## Shared Patterns

### IDisposable Temp-Dir Cleanup
**Source:** `tests/FileRevamp.Tests/Commands/RenameCommandTests.cs` lines 16–26, `tests/FileRevamp.Tests/Output/FailureLoggerTests.cs` lines 12–30
**Apply to:** `EdgeCaseIntegrationTests` and any test class that creates temp directories
```csharp
private readonly List<string> _tempDirs = new();

public void Dispose()
{
    foreach (var dir in _tempDirs)
    {
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }
}

private string CreateTempDir(params string[] files)
{
    var dir = Path.Combine(Path.GetTempPath(), $"filerevamp_test_{Guid.NewGuid():N}");
    Directory.CreateDirectory(dir);
    _tempDirs.Add(dir);
    foreach (var file in files)
        File.WriteAllText(Path.Combine(dir, file), string.Empty);
    return dir;
}
```

### FluentAssertions Assertion Style
**Source:** `tests/FileRevamp.Tests/Commands/RenameCommandTests.cs` lines 71–95
**Apply to:** All test methods in `EdgeCaseIntegrationTests`
- Use `.Should().Be(0)` not `Assert.Equal(0, ...)`
- Use `.Should().Contain("text", because: "reason string")` for output assertions
- Use `.Should().BeTrue()` / `.Should().BeFalse()` for boolean/file-existence assertions
- Chained `because:` parameter is mandatory on non-obvious assertions

### FakeTypeRegistrar DI Wiring
**Source:** `tests/FileRevamp.Tests/Commands/RenameCommandTests.cs` lines 33–50
**Apply to:** `EdgeCaseIntegrationTests.CreateTester()` (copy verbatim, extend with IFileSystem registration when needed)

### Markup.Escape Safety
**Source:** `src/FileRevamp/Commands/RenameCommand.cs` lines 110, 121, 125
**Apply to:** Any new console output in `RenameCommand.Execute` — all dynamic strings must be wrapped with `Markup.Escape()` before passing to `_console.MarkupLine()`:
```csharp
_console.MarkupLine(Markup.Escape(someUserProvidedString));
```

---

## No Analog Found

No files in Phase 3 are genuinely novel — all new files copy from or extend existing analogs above.

However, the **dotnet pack / install verification** scenario (D-01 requirement) has no analog in the test suite. This should be implemented as either:
- A PowerShell/bash script in a `scripts/` or `tools/` directory (not a C# test), or
- A note in the phase plan that pack verification is done manually (run `dotnet pack` then `dotnet tool install --global --add-source ./nupkg FileRevamp` and confirm `filerevamp --version`)

A C# test that spawns a subprocess to `dotnet pack` does not fit the `CommandAppTester` in-process pattern and has no existing analog to copy.

| File | Role | Data Flow | Reason |
|---|---|---|---|
| `scripts/verify-install.ps1` (optional) | config | — | No subprocess test analog; manual verification script is the closest fit |

---

## Metadata

**Analog search scope:** `src/FileRevamp/**`, `tests/FileRevamp.Tests/**`
**Files scanned:** 20 (13 test files + 7 src files including Program.cs and csproj)
**Pattern extraction date:** 2026-06-03
