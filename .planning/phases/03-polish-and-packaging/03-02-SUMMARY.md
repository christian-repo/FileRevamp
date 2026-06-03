---
phase: 03-polish-and-packaging
plan: "02"
subsystem: core
tags: [injection-seam, testability, packaging, nuget, cli-tool]
dependency_graph:
  requires:
    - 03-01 (MockFileSystem in test assembly, build clean)
  provides:
    - IFileSystem injection seam in RenameCommand (test can pass MockFileSystem)
    - NuGet package metadata (PackageId, Version, License, RepositoryUrl, ReadmeFile)
    - FileRevamp.1.0.0.nupkg produced by dotnet pack
  affects:
    - src/FileRevamp/Commands/RenameCommand.cs
    - src/FileRevamp/FileRevamp.csproj
tech_stack:
  added: []
  patterns:
    - Null-coalescing injection: _injectedFileSystem ?? (DryRun ? DryRunFileSystem : FileSystem)
    - PackageReadmeFile with None ItemGroup to include README.md in NuGet package
key_files:
  created: []
  modified:
    - src/FileRevamp/Commands/RenameCommand.cs
    - src/FileRevamp/FileRevamp.csproj
decisions:
  - IFileSystem injection follows the same null-coalescing pattern as IAnsiConsole; default null preserves production behavior unchanged
  - README.md included in package via None ItemGroup with PackagePath="\" as required by NuGet tooling when PackageReadmeFile is set
metrics:
  duration: "~10 minutes"
  completed_date: "2026-06-03"
  tasks_completed: 2
  tasks_total: 2
  files_changed: 2
---

# Phase 03 Plan 02: Injection Seam and Package Metadata Summary

**One-liner:** IFileSystem constructor injection added to RenameCommand with null-coalescing fallback, and full NuGet package metadata added to FileRevamp.csproj enabling dotnet pack to produce FileRevamp.1.0.0.nupkg.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Add IFileSystem injection to RenameCommand | 9c6d919 | src/FileRevamp/Commands/RenameCommand.cs |
| 2 | Add package metadata to FileRevamp.csproj and verify dotnet pack | 954225c | src/FileRevamp/FileRevamp.csproj |

## Objective Outcome

Both objectives achieved:

- **IFileSystem injection seam**: `RenameCommand` now accepts an optional `IFileSystem? fileSystem = null` second constructor parameter. The field `_injectedFileSystem` holds the injected value. File system selection uses `_injectedFileSystem ?? (settings.DryRun ? new DryRunFileSystem() : new FileSystem())`. Tests can now pass a `MockFileSystem` instance without disk access. The existing `new RenameCommand(tester.Console)` call in tests remains valid — the second parameter defaults to null and the production path is taken.

- **Package metadata**: `FileRevamp.csproj` now contains `PackageId=FileRevamp`, `Version=1.0.0`, `Authors=FileRevamp Contributors`, `Description`, `PackageLicenseExpression=MIT`, `RepositoryUrl`, and `PackageReadmeFile=README.md`. A `None` ItemGroup includes `../../README.md` with `Pack="true"` and `PackagePath="\"`. Running `dotnet pack -c Release` produces `src/FileRevamp/nupkg/FileRevamp.1.0.0.nupkg`.

## Verification

```
dotnet build   → Build succeeded, 0 warnings, 0 errors
dotnet test    → Passed! 73/73 tests green
grep "_injectedFileSystem" src/FileRevamp/Commands/RenameCommand.cs → 4 matches
grep "PackageId" src/FileRevamp/FileRevamp.csproj → match found
dotnet pack    → Successfully created package FileRevamp.1.0.0.nupkg
ls src/FileRevamp/nupkg/FileRevamp.*.nupkg → FileRevamp.1.0.0.nupkg
```

## Deviations from Plan

None — plan executed exactly as written.

## Known Stubs

None.

## Threat Flags

None — no new network endpoints, auth paths, file access patterns, or schema changes introduced. Package metadata uses MIT license and public GitHub RepositoryUrl consistent with the repo's public nature.

## Self-Check: PASSED

- `src/FileRevamp/Commands/RenameCommand.cs` contains `_injectedFileSystem` — FOUND
- `src/FileRevamp/FileRevamp.csproj` contains `PackageId` — FOUND
- `src/FileRevamp/nupkg/FileRevamp.1.0.0.nupkg` — FOUND
- Commits 9c6d919, 954225c — verified in git log
- All 73 tests green
