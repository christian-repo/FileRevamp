namespace FileRevamp.Core;

/// <summary>
/// Immutable planning record representing the resolved rename intent for a single file.
/// Produced by <see cref="RenameOrchestrator.Plan"/> and consumed by <see cref="RenameOrchestrator.Execute"/>.
/// </summary>
/// <param name="SourcePath">Full path to the source file (including directory).</param>
/// <param name="OriginalName">Bare filename before any transform (no directory component).</param>
/// <param name="ResolvedName">Collision-free destination filename computed during the plan pass (no directory component).</param>
/// <param name="WouldChange">True when the pipeline produced a different name; false when the file would be skipped.</param>
public record RenameProposal(
    string SourcePath,
    string OriginalName,
    string ResolvedName,
    bool WouldChange);
