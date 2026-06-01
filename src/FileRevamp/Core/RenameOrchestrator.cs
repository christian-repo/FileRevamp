using FileRevamp.Output;

namespace FileRevamp.Core;

/// <summary>
/// Drives the per-file rename pipeline: scan → apply patterns → apply replaces → check constraints → dry/live dispatch.
/// Injected IFileSystem determines whether renames are actually performed (production vs dry-run vs mock).
///
/// Operation order is fixed (PAT-03):
///   1. Remove patterns (WildcardPatternMatcher.ApplyRemoves)
///   2. Replace transforms (ReplaceTransform.Apply), applied in order
/// </summary>
public sealed class RenameOrchestrator
{
    private readonly IFileSystem _fileSystem;

    public RenameOrchestrator(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Executes the rename pipeline against files in <paramref name="directoryPath"/>,
    /// optionally filtered by a glob pattern.
    /// </summary>
    /// <param name="directoryPath">Absolute path to the directory to process.</param>
    /// <param name="globPattern">
    /// Optional glob pattern to filter files (e.g. <c>*.csv</c>). Pass <see langword="null"/>
    /// or <c>"*"</c> to process all files.
    /// </param>
    /// <param name="patternMatcher">Compiled pattern matcher for remove operations.</param>
    /// <param name="replaceTransforms">
    /// Ordered list of literal find/replace transforms to apply AFTER removes (PAT-03).
    /// May be empty if no replace operations were requested.
    /// </param>
    /// <param name="dryRun">
    /// When <see langword="true"/> the injected file system is expected to be a no-op implementation;
    /// results are tagged <see cref="RenameStatus.DryRun"/> and no files are modified.
    /// </param>
    /// <returns>One <see cref="RenameResult"/> per candidate file, streamed lazily.</returns>
    public IEnumerable<RenameResult> Execute(
        string directoryPath,
        string? globPattern,
        WildcardPatternMatcher patternMatcher,
        IReadOnlyList<ReplaceTransform> replaceTransforms,
        bool dryRun)
    {
        // Normalize directory path for path traversal checks (T-01-01, T-02-02)
        var normalizedDir = Path.GetFullPath(directoryPath);

        // Step 1: Enumerate files using FileDiscovery (supports glob filtering — TARG-02)
        var filePaths = new FileDiscovery(_fileSystem).GetFiles(directoryPath, globPattern).ToArray();

        foreach (var filePath in filePaths)
        {
            var filename = _fileSystem.GetFileName(filePath);

            // Step 2: Apply remove patterns (PAT-01, PAT-03 — removes BEFORE replaces)
            string newFilename;
            if (!patternMatcher.HasPatterns)
            {
                // No remove patterns registered → replace-only mode: all files are candidates.
                // The filename passes through unchanged to the replace step.
                newFilename = filename;
            }
            else
            {
                var afterRemove = patternMatcher.ApplyRemoves(filename);

                if (afterRemove is null)
                {
                    // No remove pattern matched this file — skip it.
                    yield return RenameResult.SkippedResult(filename, "No pattern matched");
                    continue;
                }

                newFilename = afterRemove;
            }

            // Step 3: Apply replace transforms in order (PAT-02, PAT-03 — after removes)
            foreach (var transform in replaceTransforms)
            {
                newFilename = transform.Apply(newFilename);
            }

            // T-02-01: Validate computed filename does not contain invalid path characters.
            var invalidChars = Path.GetInvalidFileNameChars();
            var invalidCharInName = newFilename.FirstOrDefault(c => Array.IndexOf(invalidChars, c) >= 0);
            if (invalidCharInName != default)
            {
                yield return RenameResult.FailedResult(
                    filename,
                    $"Computed filename contains invalid characters: '{invalidCharInName}'");
                continue;
            }

            // Skip if the pipeline produced no change to the filename.
            if (newFilename == filename)
            {
                yield return RenameResult.SkippedResult(filename, "Transform produced no change");
                continue;
            }

            var destPath = _fileSystem.Combine(directoryPath, newFilename);

            // T-01-01 / T-02-02: Path traversal rejection — destination must stay within source directory.
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
