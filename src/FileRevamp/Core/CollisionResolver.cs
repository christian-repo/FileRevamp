namespace FileRevamp.Core;

/// <summary>
/// Resolves filename collisions using Windows-style auto-numbering: file.csv → file(1).csv → file(2).csv → …
///
/// Checks both the in-batch claimed destinations set (intra-batch duplicates) and
/// <see cref="IFileSystem.FileExists"/> (existing files on disk) before assigning a destination name.
///
/// Source filenames are supplied via <paramref name="sourceNames"/> so that the resolver can
/// distinguish a name that is "occupied by a source file about to vacate" from a name that is
/// genuinely occupied by a pre-existing non-source file. Without this distinction, a batch rename
/// where a source file's name equals a computed destination for another source file would produce a
/// false-positive collision and silently auto-number the destination.
///
/// The <paramref name="claimedDestinations"/> set passed to <see cref="Resolve"/> is mutated in
/// place — each resolved name is added before returning so that subsequent calls for the same
/// desired name receive the next available slot.
/// </summary>
public sealed class CollisionResolver
{
    private readonly IFileSystem _fileSystem;
    private readonly HashSet<string> _sourceNames;

    /// <summary>
    /// Initialises a <see cref="CollisionResolver"/> with the set of source filenames that will
    /// vacate their names during Execute. This prevents false-positive collisions when a computed
    /// destination name matches a still-present source file that is part of the same batch.
    /// </summary>
    /// <param name="fileSystem">File system used for disk-existence checks.</param>
    /// <param name="sourceNames">
    /// The set of bare filenames (no directory component) of all source files in the batch.
    /// Must use <see cref="StringComparer.OrdinalIgnoreCase"/>.
    /// </param>
    public CollisionResolver(IFileSystem fileSystem, HashSet<string> sourceNames)
    {
        _fileSystem = fileSystem;
        _sourceNames = sourceNames;
    }

    /// <summary>
    /// Returns a collision-free filename for <paramref name="desiredName"/> in <paramref name="directoryPath"/>.
    /// Adds the resolved name to <paramref name="claimedDestinations"/> before returning.
    /// </summary>
    /// <param name="directoryPath">Directory in which the rename will occur.</param>
    /// <param name="desiredName">The filename the pipeline wants to assign (no directory component).</param>
    /// <param name="claimedDestinations">
    /// In-batch set of already-assigned destination names.
    /// Must be constructed with <see cref="StringComparer.OrdinalIgnoreCase"/> by the caller.
    /// Mutated in place: the resolved name is added before this method returns.
    /// </param>
    /// <returns>
    /// <paramref name="desiredName"/> when it is free, or <c>stem(N).ext</c> for the lowest free N otherwise.
    /// </returns>
    public string Resolve(string directoryPath, string desiredName, HashSet<string> claimedDestinations)
    {
        // Fast path: desired name is not already claimed as a destination in this batch, AND
        // either (a) it does not exist on disk at all, or (b) it exists only because it is a
        // source file that will vacate during Execute (not a pre-existing non-source file).
        var destPath = _fileSystem.Combine(directoryPath, desiredName);
        var diskOccupied = _fileSystem.FileExists(destPath) && !_sourceNames.Contains(desiredName);
        if (!claimedDestinations.Contains(desiredName) && !diskOccupied)
        {
            claimedDestinations.Add(desiredName);
            return desiredName;
        }

        // Slow path: find the first free slot — file(1).csv, file(2).csv, …
        var stem = Path.GetFileNameWithoutExtension(desiredName);
        var ext = Path.GetExtension(desiredName);   // includes leading dot, or "" if no extension

        const int MaxAttempts = 9999;
        for (var i = 1; i <= MaxAttempts; i++)
        {
            var candidate = $"{stem}({i}){ext}";
            var candidatePath = _fileSystem.Combine(directoryPath, candidate);
            var candidateDiskOccupied = _fileSystem.FileExists(candidatePath) && !_sourceNames.Contains(candidate);
            if (!claimedDestinations.Contains(candidate) && !candidateDiskOccupied)
            {
                claimedDestinations.Add(candidate);
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"Could not find a free collision-resolution slot for '{desiredName}' after {MaxAttempts} attempts.");
    }
}
