using FileRevamp.Output;

namespace FileRevamp.Core;

/// <summary>
/// Drives the per-file rename pipeline using a two-pass design.
///
/// Pass 1 — Plan(): Computes all source→destination pairs, resolves collisions via
/// CollisionResolver, returns an immutable proposal list and early-exit results.
/// No file system writes occur in this pass.
///
/// Pass 2 — Execute(): Acts on the proposal list — dry-run returns tagged results,
/// live run calls MoveFile for each proposal.
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
    /// Computes all rename proposals without touching the file system (except FileExists checks
    /// inside CollisionResolver). Collision-free destination names are resolved eagerly so that
    /// Execute() can act on a complete, validated plan.
    /// </summary>
    /// <param name="filePaths">Ordered list of full file paths to process (caller performs file discovery).</param>
    /// <param name="patternMatcher">Compiled pattern matcher for remove operations.</param>
    /// <param name="replaceTransforms">Ordered list of literal find/replace transforms (PAT-03).</param>
    /// <param name="directoryPath">Absolute path to the directory — used for path traversal checks and collision resolution.</param>
    /// <returns>
    /// Proposals: files that will be renamed (one per file, with collision-free ResolvedName).
    /// EarlyResults: files that are skipped or failed during planning (not eligible for Execute).
    /// </returns>
    public (IReadOnlyList<RenameProposal> Proposals, IReadOnlyList<RenameResult> EarlyResults)
        Plan(
            IReadOnlyList<string> filePaths,
            WildcardPatternMatcher patternMatcher,
            IReadOnlyList<ReplaceTransform> replaceTransforms,
            string directoryPath)
    {
        var normalizedDir = Path.GetFullPath(directoryPath);
        var proposals = new List<RenameProposal>();
        var earlyResults = new List<RenameResult>();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolver = new CollisionResolver(_fileSystem);

        foreach (var filePath in filePaths)
        {
            var filename = _fileSystem.GetFileName(filePath);

            // Step 1: Apply remove patterns (PAT-01, PAT-03 — removes BEFORE replaces)
            string newFilename;
            if (!patternMatcher.HasPatterns)
            {
                // No remove patterns → replace-only mode: all files are candidates.
                newFilename = filename;
            }
            else
            {
                var afterRemove = patternMatcher.ApplyRemoves(filename);
                if (afterRemove is null)
                {
                    earlyResults.Add(RenameResult.SkippedResult(filename, "No pattern matched"));
                    continue;
                }
                newFilename = afterRemove;
            }

            // Step 2: Apply replace transforms in order (PAT-02, PAT-03 — after removes)
            foreach (var transform in replaceTransforms)
            {
                newFilename = transform.Apply(newFilename);
            }

            // Validate computed filename does not have trailing dots or spaces (T-03 mitigation).
            var outputNameError = Reporter.ValidateOutputName(newFilename);
            if (outputNameError != null)
            {
                earlyResults.Add(RenameResult.FailedResult(filename, outputNameError));
                continue;
            }

            // T-02-01: Validate computed filename does not contain invalid path characters.
            var invalidChars = Path.GetInvalidFileNameChars();
            var invalidCharInName = newFilename.FirstOrDefault(c => Array.IndexOf(invalidChars, c) >= 0);
            if (invalidCharInName != default)
            {
                earlyResults.Add(RenameResult.FailedResult(
                    filename,
                    $"Computed filename contains invalid characters: '{invalidCharInName}'"));
                continue;
            }

            // Skip if the pipeline produced no change to the filename.
            if (newFilename == filename)
            {
                earlyResults.Add(RenameResult.SkippedResult(filename, "Transform produced no change"));
                continue;
            }

            // T-01-01 / T-02-02: Path traversal rejection — destination must stay within source directory.
            var destPath = _fileSystem.Combine(directoryPath, newFilename);
            var normalizedDest = Path.GetFullPath(destPath);
            var destDir = Path.GetDirectoryName(normalizedDest);
            if (!string.Equals(destDir, normalizedDir, StringComparison.OrdinalIgnoreCase))
            {
                earlyResults.Add(RenameResult.FailedResult(filename, "Path traversal rejected"));
                continue;
            }

            // Resolve collision: checks both the in-batch claimed set and disk (T-04-01).
            var resolvedName = resolver.Resolve(directoryPath, newFilename, claimed);
            proposals.Add(new RenameProposal(filePath, filename, resolvedName, WouldChange: true));
        }

        return (proposals.AsReadOnly(), earlyResults.AsReadOnly());
    }

    /// <summary>
    /// Executes a pre-computed plan: dry-run returns tagged results with no disk writes;
    /// live run calls MoveFile for each proposal. No file discovery or collision resolution
    /// occurs here — all decisions were made in Plan().
    /// </summary>
    /// <param name="proposals">Proposal list returned by Plan().</param>
    /// <param name="directoryPath">Absolute path to the directory where files will be renamed.</param>
    /// <param name="dryRun">When true, no files are moved.</param>
    public IReadOnlyList<RenameResult> Execute(
        IReadOnlyList<RenameProposal> proposals,
        string directoryPath,
        bool dryRun)
    {
        var results = new List<RenameResult>(proposals.Count);

        foreach (var proposal in proposals)
        {
            if (dryRun)
            {
                results.Add(RenameResult.DryRunResult(proposal.OriginalName, proposal.ResolvedName));
            }
            else
            {
                var destPath = _fileSystem.Combine(directoryPath, proposal.ResolvedName);
                RenameResult moveResult;
                try
                {
                    _fileSystem.MoveFile(proposal.SourcePath, destPath);
                    moveResult = RenameResult.RenamedResult(proposal.OriginalName, proposal.ResolvedName);
                }
                catch (Exception ex)
                {
                    moveResult = RenameResult.FailedResult(proposal.OriginalName, ex.Message);
                }
                results.Add(moveResult);
            }
        }

        return results.AsReadOnly();
    }
}
