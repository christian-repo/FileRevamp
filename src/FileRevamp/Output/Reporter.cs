namespace FileRevamp.Output;

/// <summary>
/// Formats per-file output lines and end-of-run summary strings.
/// This class is a pure string formatter — it has no console dependency.
/// The caller (RenameCommand) is responsible for writing the formatted strings to IAnsiConsole.
///
/// RPRT-01: Every processed file produces one formatted output line.
/// RPRT-02: End of run produces a summary line with counts.
/// </summary>
public sealed class Reporter
{
    /// <summary>
    /// Formats a single rename result into a human-readable output line.
    /// The caller must wrap this in Markup.Escape() before passing to AnsiConsole.MarkupLine
    /// to prevent Spectre markup injection from filenames containing [ or ] characters (T-03-01).
    /// </summary>
    public string FormatResultLine(RenameResult result) =>
        result.Status switch
        {
            RenameStatus.DryRun  => $"[DRY RUN] {result.OriginalName} → {result.NewName}",
            RenameStatus.Renamed => $"{result.OriginalName} → {result.NewName}",
            RenameStatus.Skipped => $"SKIP {result.OriginalName}: {result.FailureReason}",
            RenameStatus.Failed  => $"FAIL {result.OriginalName}: {result.FailureReason}",
            _                    => $"UNKNOWN {result.OriginalName}"
        };

    /// <summary>
    /// Formats the end-of-run summary line showing counts of each status.
    /// </summary>
    public string FormatSummary(IEnumerable<RenameResult> results)
    {
        var list = results.ToList();
        var renamed = list.Count(r => r.Status == RenameStatus.Renamed);
        var failed  = list.Count(r => r.Status == RenameStatus.Failed);
        var skipped = list.Count(r => r.Status == RenameStatus.Skipped);
        return $"Renamed: {renamed}  Failed: {failed}  Skipped: {skipped}";
    }

    /// <summary>
    /// Returns the dry-run completion message.
    /// </summary>
    public string FormatDryRunComplete() =>
        "Dry run complete — 0 files modified.";

    /// <summary>
    /// Validates that a computed output filename is safe to use on Windows.
    /// Returns null if the name is valid; returns a human-readable error message otherwise.
    /// Addresses Pitfall 6 (trailing dots/spaces silently stripped by Win32 API).
    /// </summary>
    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string? ValidateOutputName(string filename)
    {
        if (string.IsNullOrEmpty(filename))
            return "Computed filename is empty";

        if (filename.EndsWith('.'))
            return $"Computed filename has trailing dot: '{filename}'";

        if (filename.EndsWith(' '))
            return $"Computed filename has trailing space: '{filename}'";

        // CR-03: Prevent renaming to an extension-only name (e.g. ".csv") which creates a
        // hidden/inaccessible file. This occurs when remove patterns erase the entire stem.
        var stem = Path.GetFileNameWithoutExtension(filename);
        if (string.IsNullOrEmpty(stem))
            return $"Computed filename has empty stem (would produce extension-only file): '{filename}'";

        // WR-04: Reject Windows reserved device names (CON, NUL, COM1, etc.) which cannot
        // be created as regular files and are permanently inaccessible through normal APIs.
        if (WindowsReservedNames.Contains(stem))
            return $"Computed filename '{filename}' uses a Windows reserved device name";

        return null;
    }
}
