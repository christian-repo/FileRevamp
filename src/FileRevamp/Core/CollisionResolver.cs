namespace FileRevamp.Core;

/// <summary>
/// Resolves filename collisions using Windows-style auto-numbering: file.csv → file(1).csv → file(2).csv → …
///
/// Checks both the in-batch claimed set (intra-batch duplicates) and <see cref="IFileSystem.FileExists"/>
/// (existing files on disk) before assigning a destination name.
///
/// The <paramref name="claimed"/> set passed to <see cref="Resolve"/> is mutated in place — each resolved
/// name is added to <paramref name="claimed"/> before returning so that subsequent calls for the same
/// desired name receive the next available slot.
/// </summary>
public sealed class CollisionResolver
{
    private readonly IFileSystem _fileSystem;

    public CollisionResolver(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Returns a collision-free filename for <paramref name="desiredName"/> in <paramref name="directoryPath"/>.
    /// Adds the resolved name to <paramref name="claimed"/> before returning.
    /// </summary>
    /// <param name="directoryPath">Directory in which the rename will occur.</param>
    /// <param name="desiredName">The filename the pipeline wants to assign (no directory component).</param>
    /// <param name="claimed">
    /// In-batch set of already-assigned destination names.
    /// Must be constructed with <see cref="StringComparer.OrdinalIgnoreCase"/> by the caller.
    /// Mutated in place: the resolved name is added before this method returns.
    /// </param>
    /// <returns>
    /// <paramref name="desiredName"/> when it is free, or <c>stem(N).ext</c> for the lowest free N otherwise.
    /// </returns>
    public string Resolve(string directoryPath, string desiredName, HashSet<string> claimed)
    {
        // Fast path: desired name is free on disk and not yet claimed in this batch.
        var destPath = _fileSystem.Combine(directoryPath, desiredName);
        if (!claimed.Contains(desiredName) && !_fileSystem.FileExists(destPath))
        {
            claimed.Add(desiredName);
            return desiredName;
        }

        // Slow path: find the first free slot — file(1).csv, file(2).csv, …
        var stem = Path.GetFileNameWithoutExtension(desiredName);
        var ext = Path.GetExtension(desiredName);   // includes leading dot, or "" if no extension

        for (var i = 1; ; i++)
        {
            var candidate = $"{stem}({i}){ext}";
            var candidatePath = _fileSystem.Combine(directoryPath, candidate);
            if (!claimed.Contains(candidate) && !_fileSystem.FileExists(candidatePath))
            {
                claimed.Add(candidate);
                return candidate;
            }
        }
    }
}
