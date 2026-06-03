using FileRevamp.Core;
using FileRevamp.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FileRevamp.Commands;

/// <summary>
/// Main CLI command: scans a directory and applies remove patterns and replace transforms to filenames.
/// In dry-run mode no files are modified; results are tagged [DRY RUN].
/// Operation order is fixed: removes fire first, then replaces (PAT-03).
/// </summary>
public sealed class RenameCommand : Command<RenameSettings>
{
    private readonly IAnsiConsole _console;

    /// <summary>
    /// Initializes RenameCommand with an optional injected IAnsiConsole.
    /// When <paramref name="console"/> is null (production, no DI registrar), falls back to
    /// AnsiConsole.Console (the global terminal). When injected (tests), uses the provided
    /// instance so CommandAppTester can capture output.
    /// </summary>
    public RenameCommand(IAnsiConsole? console = null)
    {
        _console = console ?? AnsiConsole.Console;
    }

    protected override int Execute(CommandContext context, RenameSettings settings, CancellationToken cancellationToken = default)
    {
        // Determine if settings.Path contains a glob pattern (e.g. /exports/*.csv).
        // If it does, split into directory + glob. Otherwise treat the whole path as a directory.
        string directoryPath;
        string? globPattern = null;

        var rawPath = settings.Path;
        if (rawPath.Contains('*') || rawPath.Contains('?'))
        {
            // Path contains glob chars — split into directory and glob pattern.
            var dir = System.IO.Path.GetDirectoryName(rawPath);
            globPattern = System.IO.Path.GetFileName(rawPath);
            directoryPath = string.IsNullOrEmpty(dir) ? "." : dir;
        }
        else
        {
            directoryPath = rawPath;
        }

        // T-01-02: Resolve to absolute path at entry point.
        directoryPath = System.IO.Path.GetFullPath(directoryPath);

        // Validate the directory exists (Phase 1: directory-only mode).
        if (!Directory.Exists(directoryPath))
        {
            _console.MarkupLine($"[yellow]Directory not found: {Markup.Escape(directoryPath)}. No files to process.[/]");
            return 0;
        }

        // Choose the file system implementation based on dry-run flag.
        IFileSystem fileSystem = settings.DryRun
            ? new DryRunFileSystem()
            : new FileSystem();

        var patternMatcher = new WildcardPatternMatcher(
            settings.RemovePatterns ?? Array.Empty<string>());

        // Parse --replace operands into ReplaceTransform instances.
        // Format: "old->new" — split on first "->".
        // Track whether any operand failed to parse; return exit code 1 if so (CR-01).
        var replaceParseError = false;
        var replaceTransforms = (settings.ReplaceOperations ?? Array.Empty<string>())
            .Select(op =>
            {
                try
                {
                    return ReplaceTransform.Parse(op);
                }
                catch (ArgumentException ex)
                {
                    _console.MarkupLine($"[red]Invalid --replace operand '{Markup.Escape(op)}': {Markup.Escape(ex.Message)}[/]");
                    replaceParseError = true;
                    return null;
                }
            })
            .Where(t => t is not null)
            .Cast<ReplaceTransform>()
            .ToList();

        if (replaceParseError)
            return 1;

        var reporter = new Reporter();
        var orchestrator = new RenameOrchestrator(fileSystem);
        var failureLogger = new FailureLogger(directoryPath);

        // Log file is excluded from the batch so it is never a rename candidate (RPRT-03).
        const string LogFileName = "rename-failures.log";
        var filePaths = new FileDiscovery(fileSystem)
            .GetFiles(directoryPath, globPattern)
            .Where(p => !string.Equals(System.IO.Path.GetFileName(p), LogFileName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var (proposals, earlyResults) = orchestrator.Plan(filePaths, patternMatcher, replaceTransforms, directoryPath);
        var executeResults = orchestrator.Execute(proposals, directoryPath, settings.DryRun);
        var results = earlyResults.Concat(executeResults).ToList();

        foreach (var result in results)
        {
            // T-03-01: Wrap in Markup.Escape() to prevent Spectre markup injection from filenames
            // containing [ or ] characters.
            _console.MarkupLine(Markup.Escape(reporter.FormatResultLine(result)));
        }

        // Log failures to rename-failures.log (lazy — file not created when all renames succeed).
        foreach (var result in results.Where(r => r.Status == RenameStatus.Failed))
        {
            failureLogger.Log(result.OriginalName, result.FailureReason ?? "Unknown error");
        }

        if (settings.DryRun)
        {
            _console.MarkupLine(Markup.Escape(reporter.FormatDryRunComplete(results)));
        }
        else
        {
            _console.MarkupLine(Markup.Escape(reporter.FormatSummary(results)));
        }

        var failed = results.Count(r => r.Status == RenameStatus.Failed);
        return failed > 0 ? 1 : 0;
    }
}
