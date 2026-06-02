# Technology Stack

**Project:** FileRevamp
**Researched:** 2026-05-31
**Confidence:** HIGH (all primary claims verified against official NuGet, Microsoft Docs, and official project documentation)

---

## Recommended Stack

### Core Framework

| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| .NET | 9.0 | Runtime and SDK | .NET 9 is current (STS, Nov 2025); .NET 8 is LTS but .NET 9 offers better startup performance, which matters for a CLI tool invoked per-command. For a small standalone tool with no enterprise lifecycle requirements, the performance win outweighs LTS concerns. Use `net9.0` TFM. |
| C# | 13 | Language | Ships with .NET 9 SDK; required for `ConsoleAppFramework` v5 source generators if that route is taken. No breaking changes from C# 12 for this use case. |

### CLI Argument Parsing

| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| **Spectre.Console.Cli** | **0.55.0** | Command-line argument parsing | See full rationale below. |
| Spectre.Console | 0.55.0 | Console output formatting | Ships as a dependency of Spectre.Console.Cli; provides ANSI color, tables, and markup for the rename preview output and summary. |

**CLI Framework Decision — Spectre.Console.Cli over all alternatives:**

- **System.CommandLine 2.0.8** — Only reached stable in May 2026 after years in beta. The API is discoverable but verbose; wiring up commands requires boilerplate in `Program.cs`. It has no built-in console output facilities, so a separate Spectre.Console dependency would still be needed for colored output. Good library, but for a tool that also needs rich console output, Spectre.Console.Cli replaces it entirely. Confidence: HIGH.

- **Spectre.Console.Cli 0.55.0** — Recommended. The `Command<TSettings>` + `CommandSettings` pattern separates argument declaration from execution cleanly. Auto-generates help text from `[Description]` attributes. Integrates directly with Spectre.Console for markup, tables, and status displays. Active maintenance (0.55 released April 2026). The project was separated into its own repository in 0.54 (Nov 2025) and is on a path to a 1.0 release. Single NuGet dependency covers both arg parsing and rich output. Confidence: HIGH.

- **ConsoleAppFramework v5 (5.7.13)** — AOT-safe, source-generator-based, zero reflection, extremely fast startup. The right choice if you need Native AOT or a distributable self-contained binary under 5MB. For FileRevamp, the PROJECT.md says "no external dependencies beyond the BCL preferred" and targets power users who will install it as a dotnet tool — not a trimmed binary. ConsoleAppFramework's source-generator approach also adds complexity to the build that is not warranted here. Confidence: HIGH.

- **Cocona** — Minimal framework, convenient method-attribute approach. Less mature, smaller ecosystem, no built-in rich output. No reason to choose it over Spectre.Console.Cli. Confidence: MEDIUM.

- **CliFx** — Type-safe, testable. Smaller community than Spectre. No built-in output. Confidence: MEDIUM.

### Pattern Matching

| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| `System.Text.RegularExpressions.Regex` | BCL (in-box) | Full regex matching and replace via `--advanced` flag | Already in BCL; `[GeneratedRegex]` attribute in .NET 7+ compiles patterns at build time for zero-allocation matching. Named capture groups (`(?<name>...)`) and `${name}` substitution syntax cover all replace-transform requirements. No NuGet dependency needed. |
| Custom wildcard-to-regex translator | (hand-rolled, ~30 lines) | Convert `{*}`, `{+}`, `{?}` syntax to regex | The project's wildcard syntax (`{*}` = `.*`, `{+}` = `.+`, `{?}` = `.?`) maps directly to regex quantifiers. Translating on input is trivial and produces a standard `Regex` object, keeping the execution path uniform. Do NOT use `Microsoft.Extensions.FileSystemGlobbing` for this — that library is for selecting which files to process (directory traversal), not for transforming filenames. See below. |

**Glob vs Regex distinction — important:**

The project uses patterns in two distinct ways:

1. **Target selection** — "which files in this directory to process" (e.g., `*.csv`, `**/*.txt`). This is a filesystem glob. Use `Microsoft.Extensions.FileSystemGlobbing` (see below).
2. **Filename transformation** — "what to remove/replace within a filename" (e.g., `_{*}new_{*}`). This is a name-pattern match. Use `System.Text.RegularExpressions.Regex` after translating the wildcard syntax.

Conflating these two uses is a common design mistake (see PITFALLS.md).

### File Discovery

| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| `Microsoft.Extensions.FileSystemGlobbing` | 9.0.x (bundled with .NET 9 SDK) | Enumerate files matching a glob pattern in a directory | Official Microsoft library. Supports `*`, `**`, `?`, relative paths. Available as an in-box package reference with no additional NuGet download when targeting net9.0. Handles `--recursive` behavior via `**/*` patterns. The `Matcher` class is well-tested and cross-platform. |

The package version tracks the .NET runtime version; on net9.0 you reference `Microsoft.Extensions.FileSystemGlobbing` without specifying a version and receive 9.x automatically.

### Output and Logging

| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| Spectre.Console (via Spectre.Console.Cli) | 0.55.0 | Console output: rename preview table, summary, error markup | Already a dependency. `AnsiConsole.MarkupLine` for inline color, `Table` for dry-run preview, `Rule` for section separators. |
| `System.IO.StreamWriter` / `File.AppendAllText` | BCL | Log file in target directory | Plain-text log is one-liner BCL. No logging framework (Serilog, NLog) is warranted for a file that just records `filename: reason`. Adding a logging framework would be over-engineering. |

### Testing

| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| xUnit | 2.9.x | Test runner | De facto standard for .NET. Ships with `dotnet new xunit`. Parallel test execution by default. |
| FluentAssertions | 7.x | Assertion library | Readable assertion syntax (`result.Should().Be(...)`). Significantly improves test failure messages. Standard companion to xUnit. |
| Spectre.Console.Cli.Testing | 0.55.0 | CLI command test harness | Introduced in Spectre 0.54+; provides `CommandAppTester` to run commands in-process and capture output without spawning a subprocess. Essential for testing `--dry-run` output and argument validation. |

---

## Alternatives Considered

| Category | Recommended | Alternative | Why Not |
|----------|-------------|-------------|---------|
| CLI framework | Spectre.Console.Cli 0.55.0 | System.CommandLine 2.0.8 | Still needs Spectre for output; double dependency with no gain |
| CLI framework | Spectre.Console.Cli 0.55.0 | ConsoleAppFramework v5 | AOT/zero-dep value doesn't apply; adds build complexity |
| CLI framework | Spectre.Console.Cli 0.55.0 | Cocona | Smaller ecosystem, no rich output |
| Pattern matching | BCL Regex + custom translator | DotNet.Glob (NuGet) | External dependency; doesn't cover filename-transform use case anyway |
| File discovery | FileSystemGlobbing (in-box) | Directory.GetFiles with SearchPattern | Only supports `*` and `?`; no `**` recursive depth; no exclude patterns |
| Logging | BCL File.AppendAllText | Serilog / NLog / Microsoft.Extensions.Logging | Severe over-engineering for a single-purpose text log file |
| Target framework | net9.0 | net8.0 (LTS) | A CLI tool installed by power users has no enterprise lifecycle constraint; net9.0 performance wins (startup, allocation) matter more |

---

## Project Structure

```
FileRevamp/
├── FileRevamp.sln
├── src/
│   └── FileRevamp/
│       ├── FileRevamp.csproj
│       ├── Program.cs                  # Entry point: builds CommandApp, registers commands
│       ├── Commands/
│       │   ├── RenameCommand.cs        # Command<RenameSettings> — main execution
│       │   └── RenameSettings.cs      # CommandSettings — all flags and arguments
│       ├── Core/
│       │   ├── FileDiscovery.cs        # Wraps FileSystemGlobbing Matcher
│       │   ├── PatternTranslator.cs    # Translates {*}/{+}/{?} wildcard syntax to Regex
│       │   ├── RenameEngine.cs         # Applies removes then replacements; handles conflicts
│       │   └── ConflictResolver.cs     # Auto-numbers new names (file(1).csv logic)
│       └── Output/
│           ├── RenameResult.cs         # Value object: original name, new name, success/failure
│           └── ResultLogger.cs        # Writes failure log to target directory
└── tests/
    └── FileRevamp.Tests/
        ├── FileRevamp.Tests.csproj
        ├── Core/
        │   ├── PatternTranslatorTests.cs
        │   ├── RenameEngineTests.cs
        │   └── ConflictResolverTests.cs
        └── Commands/
            └── RenameCommandTests.cs   # Uses Spectre.Console.Cli.Testing CommandAppTester
```

**Rationale for this structure:**

- `Commands/` contains exactly what Spectre.Console.Cli needs: one `Command<T>` and one `CommandSettings`. Keep it thin — validation and parsing only.
- `Core/` contains all business logic with no dependency on Spectre. This makes unit testing the rename engine trivial (no CLI harness required).
- `Output/` separates the result model and log-writing from business logic. The rename engine returns `RenameResult[]`; the command decides how to display and log them.
- No separate projects (no `FileRevamp.Core.csproj`). This is a small CLI tool, not a Clean Architecture service. A single project with namespace separation is the right call at this scope.

---

## csproj Configuration

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RollForward>Major</RollForward>

    <!-- dotnet tool packaging -->
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>filerevamp</ToolCommandName>
    <PackageOutputPath>./nupkg</PackageOutputPath>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Spectre.Console.Cli" Version="0.55.0" />
    <PackageReference Include="Microsoft.Extensions.FileSystemGlobbing" Version="9.*" />
  </ItemGroup>

</Project>
```

Notes:
- `RollForward=Major` allows the tool to run on .NET 10+ without reinstall.
- `Microsoft.Extensions.FileSystemGlobbing` is technically in-box for net9.0 apps but must be referenced explicitly as a NuGet package. The `9.*` floating version picks up patch updates automatically.
- No `<Nullable>enable</Nullable>` is needed separately — it is included in the block above.

---

## Installation

```bash
# Restore
dotnet restore

# Run in development
dotnet run --project src/FileRevamp -- rename ./exports --remove "_{*}new_{*}" --dry-run

# Pack as dotnet tool
dotnet pack src/FileRevamp -c Release

# Install locally for testing
dotnet tool install --global --add-source ./src/FileRevamp/nupkg filerevamp
```

---

## Confidence Summary

| Decision | Confidence | Source |
|----------|------------|--------|
| Spectre.Console.Cli 0.55.0 as CLI framework | HIGH | NuGet Gallery (April 2026 release confirmed), spectreconsole.net/cli docs |
| System.CommandLine 2.0.8 as rejected alternative | HIGH | NuGet Gallery (stable May 2026); GitHub issues on API verbosity |
| .NET 9 / net9.0 TFM | HIGH | Microsoft Docs — .NET versions; STS acceptable for tool scope |
| BCL Regex for filename transforms | HIGH | Microsoft Docs — System.Text.RegularExpressions; no external dep needed |
| Microsoft.Extensions.FileSystemGlobbing for file selection | HIGH | Microsoft Learn file-globbing docs; NuGet 9.0.x current |
| xUnit + FluentAssertions + Spectre.Testing for tests | HIGH | Standard community practice; Spectre.Testing confirmed in 0.54+ release notes |
| Single-project structure | MEDIUM | Community consensus for small CLI tools; Jason Taylor console template as reference |

---

## Sources

- [NuGet Gallery: System.CommandLine 2.0.8](https://www.nuget.org/packages/System.CommandLine/) — confirmed stable, May 2026
- [NuGet Gallery: Spectre.Console.Cli 0.55.0](https://www.nuget.org/packages/Spectre.Console.Cli/) — confirmed stable, April 2026
- [Spectre.Console.Cli Documentation](https://spectreconsole.net/cli) — Command/Settings pattern
- [Spectre.Console 0.54.0 release notes](https://spectreconsole.net/blog/2025-11-13-spectre-console-0-54-released) — split repo, Cli.Testing introduced
- [File globbing in .NET — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/extensions/file-globbing) — Matcher API, pattern formats
- [Tutorial: Create a .NET tool — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools-how-to-create) — PackAsTool, ToolCommandName properties
- [ConsoleAppFramework v5 — Medium](https://neuecc.medium.com/consoleappframework-v5-zero-overhead-native-aot-compatible-cli-framework-for-c-8f496df8d9d1) — AOT alternative considered
- [NuGet Gallery: ConsoleAppFramework 5.7.13](https://www.nuget.org/packages/ConsoleAppFramework) — version confirmed
- [System.Text.RegularExpressions.Regex.Replace — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/system.text.regularexpressions.regex.replace)
- [Substitutions in Regular Expressions — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/standard/base-types/substitutions-in-regular-expressions) — `${name}` group substitution syntax
