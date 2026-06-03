namespace FileRevamp.Output;

/// <summary>
/// Appends one timestamped failure line per call to rename-failures.log in the target directory.
/// The log file is created lazily on the first Log() call — if no failures occur, no file is written (RPRT-03).
/// Uses BCL File.AppendAllText directly; does not inject IFileSystem because the failure log
/// is a diagnostic output, not part of the rename operation, and must be written even in dry-run mode.
/// </summary>
public sealed class FailureLogger
{
    private readonly string _logFilePath;

    public FailureLogger(string directoryPath)
    {
        _logFilePath = Path.Combine(directoryPath, "rename-failures.log");
    }

    /// <summary>
    /// Full path to the log file: Path.Combine(directoryPath, "rename-failures.log").
    /// The file may not exist yet if Log() has not been called.
    /// </summary>
    public string LogFilePath => _logFilePath;

    /// <summary>
    /// Appends a single formatted failure line to rename-failures.log.
    /// Creates the file on the first call if it does not exist (lazy creation — Pitfall 6).
    /// Format: "[YYYY-MM-DD HH:mm:ss] FAIL {originalName}: {reason}"
    /// </summary>
    public void Log(string originalName, string reason)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] FAIL {originalName}: {reason}";
        File.AppendAllText(_logFilePath, line + Environment.NewLine);
    }
}
