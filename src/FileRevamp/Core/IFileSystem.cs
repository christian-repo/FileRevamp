namespace FileRevamp.Core;

/// <summary>
/// Abstraction over file system I/O — the single dry-run/test seam.
/// Production code injects FileSystem; dry-run injects DryRunFileSystem; tests inject MockFileSystem.
/// </summary>
public interface IFileSystem
{
    /// <summary>Returns the full paths of all files in the given directory matching the search pattern.</summary>
    IEnumerable<string> GetFiles(string directoryPath, string searchPattern);

    /// <summary>Moves (renames) a file. In dry-run mode this is a no-op.</summary>
    void MoveFile(string sourcePath, string destPath);

    /// <summary>Returns true if the file exists at the given path.</summary>
    bool FileExists(string path);

    /// <summary>Returns just the filename portion of a path (no directory component).</summary>
    string GetFileName(string path);

    /// <summary>Combines a directory path and a filename into a full path.</summary>
    string Combine(string dir, string filename);
}
