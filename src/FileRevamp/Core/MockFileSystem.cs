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
            _files[Normalize(path)] = true;
    }

    public IEnumerable<string> GetFiles(string directoryPath, string searchPattern)
    {
        // Normalize separators to forward slash for comparison (tests use Unix-style paths).
        var normalizedDir = Normalize(directoryPath).TrimEnd('/');

        // When directoryPath is "/" or empty return all files (test convenience).
        if (normalizedDir == string.Empty || normalizedDir == "/")
            return _files.Keys;

        return _files.Keys.Where(path =>
        {
            var dir = Normalize(Path.GetDirectoryName(path) ?? string.Empty).TrimEnd('/');
            return string.Equals(dir, normalizedDir, StringComparison.OrdinalIgnoreCase);
        });
    }

    public void MoveFile(string sourcePath, string destPath)
    {
        _files.Remove(Normalize(sourcePath));
        _files[Normalize(destPath)] = true;
        MoveCallCount++;
    }

    public bool FileExists(string path) => _files.ContainsKey(Normalize(path));

    public string GetFileName(string path) => Path.GetFileName(path) ?? path;

    public string Combine(string dir, string filename) =>
        Normalize(dir).TrimEnd('/') + "/" + filename;

    private static string Normalize(string path) => path.Replace('\\', '/');
}
