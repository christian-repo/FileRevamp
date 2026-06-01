namespace FileRevamp.Output;

/// <summary>
/// Status of a single rename operation.
/// </summary>
public enum RenameStatus
{
    Renamed,
    DryRun,
    Skipped,
    Failed
}

/// <summary>
/// Immutable value object representing the outcome of a single file rename attempt.
/// </summary>
/// <param name="OriginalName">The filename before the operation (no directory).</param>
/// <param name="NewName">The filename after the operation (no directory). Same as OriginalName when Skipped or Failed.</param>
/// <param name="Status">Outcome of the operation.</param>
/// <param name="FailureReason">Human-readable reason for Skipped or Failed status; null when Renamed or DryRun.</param>
public record RenameResult(
    string OriginalName,
    string NewName,
    RenameStatus Status,
    string? FailureReason = null)
{
    /// <summary>Creates a dry-run preview result.</summary>
    public static RenameResult DryRunResult(string original, string newName) =>
        new(original, newName, RenameStatus.DryRun);

    /// <summary>Creates a successfully renamed result.</summary>
    public static RenameResult RenamedResult(string original, string newName) =>
        new(original, newName, RenameStatus.Renamed);

    /// <summary>Creates a skipped result with a reason.</summary>
    public static RenameResult SkippedResult(string original, string reason) =>
        new(original, original, RenameStatus.Skipped, reason);

    /// <summary>Creates a failed result with the error message.</summary>
    public static RenameResult FailedResult(string original, string reason) =>
        new(original, original, RenameStatus.Failed, reason);
}
