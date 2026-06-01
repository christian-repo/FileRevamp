namespace FileRevamp.Core;

/// <summary>
/// In-memory file system for unit tests. No disk access.
/// Tracks MoveCallCount to allow assertions that dry-run does not move files.
/// </summary>
public sealed class MockFileSystem : IFileSystem
{
    private readonly Dictionary<string, bool> _files =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Number of times MoveFile has been called.</summary>
    public int MoveCallCount { get; private set; }

    /// <summary>Initialises the mock with a collection of pre-existing file paths.</summary>
    public MockFileSystem(IEnumerable<string> initialFiles)
    {
        foreach (var path in initialFiles)
            _files[path] = true;
    }

    public IEnumerable<string> GetFiles(string directoryPath, string searchPattern)
    {
        // Return all files whose directory component matches the requested directory.
        // When directoryPath is "/" return all files (test convenience).
        return _files.Keys.Where(path =>
            directoryPath == "/"
            || string.Equals(
                Path.GetDirectoryName(path)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase));
    }

    public void MoveFile(string sourcePath, string destPath)
    {
        _files.Remove(sourcePath);
        _files[destPath] = true;
        MoveCallCount++;
    }

    public bool FileExists(string path) => _files.ContainsKey(path);

    public string GetFileName(string path) => Path.GetFileName(path) ?? path;

    public string Combine(string dir, string filename) => Path.Combine(dir, filename);
}
