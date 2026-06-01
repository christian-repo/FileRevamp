using FileRevamp.Output;

namespace FileRevamp.Core;

/// <summary>
/// Drives the per-file rename pipeline: scan → apply patterns → check constraints → dry/live dispatch.
/// Injected IFileSystem determines whether renames are actually performed (production vs dry-run vs mock).
/// </summary>
public sealed class RenameOrchestrator
{
    private readonly IFileSystem _fileSystem;

    public RenameOrchestrator(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Executes the rename pipeline against all files in <paramref name="directoryPath"/>.
    /// </summary>
    /// <param name="directoryPath">Absolute path to the directory to process.</param>
    /// <param name="patternMatcher">Compiled pattern matcher for remove operations.</param>
    /// <param name="dryRun">
    /// When <see langword="true"/> the injected file system is expected to be a no-op implementation;
    /// results are tagged <see cref="RenameStatus.DryRun"/> and no files are modified.
    /// </param>
    /// <returns>One <see cref="RenameResult"/> per candidate file, streamed lazily.</returns>
    public IEnumerable<RenameResult> Execute(
        string directoryPath,
        WildcardPatternMatcher patternMatcher,
        bool dryRun)
    {
        // Normalize directory path for path traversal checks (T-01-01)
        var normalizedDir = Path.GetFullPath(directoryPath);

        var filePaths = _fileSystem.GetFiles(directoryPath, "*").ToArray();

        foreach (var filePath in filePaths)
        {
            var filename = _fileSystem.GetFileName(filePath);
            var newFilename = patternMatcher.ApplyRemoves(filename);

            // No pattern matched at all — skip
            if (newFilename is null)
            {
                yield return RenameResult.SkippedResult(filename, "No pattern matched");
                continue;
            }

            // Pattern matched but produced no change to the filename — skip
            if (newFilename == filename)
            {
                yield return RenameResult.SkippedResult(filename, "Transform produced no change");
                continue;
            }

            var destPath = _fileSystem.Combine(directoryPath, newFilename);

            // T-01-01: Path traversal rejection — destination must stay within source directory
            var normalizedDest = Path.GetFullPath(destPath);
            var destDir = Path.GetDirectoryName(normalizedDest);
            if (!string.Equals(destDir, normalizedDir, StringComparison.OrdinalIgnoreCase))
            {
                yield return RenameResult.FailedResult(filename, "Path traversal rejected");
                continue;
            }

            // Conflict: destination already exists (Phase 2 will add auto-numbering)
            if (_fileSystem.FileExists(destPath))
            {
                yield return RenameResult.SkippedResult(
                    filename,
                    "Destination already exists (conflict resolution coming in Phase 2)");
                continue;
            }

            if (dryRun)
            {
                yield return RenameResult.DryRunResult(filename, newFilename);
            }
            else
            {
                // C# does not allow yield in try/catch; capture result separately.
                RenameResult moveResult;
                try
                {
                    _fileSystem.MoveFile(filePath, destPath);
                    moveResult = RenameResult.RenamedResult(filename, newFilename);
                }
                catch (Exception ex)
                {
                    moveResult = RenameResult.FailedResult(filename, ex.Message);
                }

                yield return moveResult;
            }
        }
    }
}
