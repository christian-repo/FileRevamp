namespace FileRevamp.Core;

/// <summary>
/// Dry-run file system implementation: delegates all reads to the real FileSystem
/// but makes MoveFile a no-op. Injected when --dry-run is specified.
/// </summary>
public sealed class DryRunFileSystem : IFileSystem
{
    private readonly FileSystem _inner = new();

    public IEnumerable<string> GetFiles(string directoryPath, string searchPattern) =>
        _inner.GetFiles(directoryPath, searchPattern);

    /// <summary>No-op: dry-run mode never modifies files.</summary>
    public void MoveFile(string sourcePath, string destPath)
    {
        // Intentionally empty — dry run does not move files.
    }

    public bool FileExists(string path) =>
        _inner.FileExists(path);

    public string GetFileName(string path) =>
        _inner.GetFileName(path);

    public string Combine(string dir, string filename) =>
        _inner.Combine(dir, filename);
}
