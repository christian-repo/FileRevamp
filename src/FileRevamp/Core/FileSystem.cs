namespace FileRevamp.Core;

/// <summary>
/// Real file system implementation using System.IO. Injected in production runs.
/// </summary>
public sealed class FileSystem : IFileSystem
{
    public IEnumerable<string> GetFiles(string directoryPath) =>
        Directory.GetFiles(directoryPath, "*", SearchOption.TopDirectoryOnly);

    public void MoveFile(string sourcePath, string destPath) =>
        File.Move(sourcePath, destPath);

    public bool FileExists(string path) =>
        File.Exists(path);

    public string GetFileName(string path) =>
        Path.GetFileName(path) ?? path;

    public string Combine(string dir, string filename) =>
        Path.Combine(dir, filename);
}
