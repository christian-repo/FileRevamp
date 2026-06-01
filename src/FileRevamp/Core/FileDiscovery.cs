using Microsoft.Extensions.FileSystemGlobbing;

namespace FileRevamp.Core;

/// <summary>
/// Enumerates files in a directory, optionally filtered by a glob pattern.
///
/// Delegates actual I/O to the injected <see cref="IFileSystem"/> so the same class
/// works against both the real filesystem and the test <see cref="MockFileSystem"/>.
///
/// Glob filtering uses <see cref="Microsoft.Extensions.FileSystemGlobbing.Matcher"/> which
/// supports standard glob syntax: *, **, ?, character classes.
/// Note: the glob pattern is for FILE SELECTION (which files to process), NOT for the
/// wildcard REMOVE patterns that transform filenames (Pitfall 11 distinction).
/// </summary>
public sealed class FileDiscovery
{
    private readonly IFileSystem _fileSystem;

    /// <summary>
    /// Initialises a <see cref="FileDiscovery"/> instance backed by the given file system.
    /// </summary>
    /// <param name="fileSystem">The file system to enumerate files from.</param>
    public FileDiscovery(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Returns the paths of all files in <paramref name="directoryPath"/> that match
    /// the given glob pattern.
    /// </summary>
    /// <param name="directoryPath">The directory to enumerate.</param>
    /// <param name="globPattern">
    /// An optional glob pattern (e.g. <c>*.csv</c>, <c>report_*</c>). When <see langword="null"/>
    /// or <c>"*"</c>, all files are returned without any glob filtering.
    /// </param>
    /// <returns>Full paths of matching files, in the order returned by the underlying file system.</returns>
    public IEnumerable<string> GetFiles(string directoryPath, string? globPattern)
    {
        // When no filter is requested, return all files directly.
        if (string.IsNullOrEmpty(globPattern) || globPattern == "*")
            return _fileSystem.GetFiles(directoryPath, "*");

        // Use Microsoft.Extensions.FileSystemGlobbing.Matcher for non-trivial patterns.
        // Matcher.Match() accepts a base directory and a set of relative paths.
        // We pass filenames only (flat directory — no subdirectory traversal in Phase 1).
        var allFiles = _fileSystem.GetFiles(directoryPath, "*").ToArray();
        var fileNames = allFiles.Select(p => _fileSystem.GetFileName(p));

        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddInclude(globPattern);

        var matchResult = matcher.Match(fileNames);

        // FilePatternMatch.Path contains the relative path passed to Match() — which is just the filename.
        return matchResult.Files.Select(f => _fileSystem.Combine(directoryPath, f.Path));
    }
}
