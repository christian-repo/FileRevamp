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
            AnsiConsole.MarkupLine($"[yellow]Directory not found: {Markup.Escape(directoryPath)}. No files to process.[/]");
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
        var replaceTransforms = (settings.ReplaceOperations ?? Array.Empty<string>())
            .Select(op =>
            {
                try
                {
                    return ReplaceTransform.Parse(op);
                }
                catch (ArgumentException ex)
                {
                    AnsiConsole.MarkupLine($"[red]Invalid --replace operand '{Markup.Escape(op)}': {Markup.Escape(ex.Message)}[/]");
                    return null;
                }
            })
            .Where(t => t is not null)
            .Cast<ReplaceTransform>()
            .ToList();

        var orchestrator = new RenameOrchestrator(fileSystem);
        var results = orchestrator.Execute(directoryPath, globPattern, patternMatcher, replaceTransforms, settings.DryRun);

        var renamed = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var result in results)
        {
            switch (result.Status)
            {
                case RenameStatus.DryRun:
                    AnsiConsole.MarkupLine(
                        $"[yellow][[DRY RUN]][/] [grey]{Markup.Escape(result.OriginalName)}[/] [dim]→[/] [green]{Markup.Escape(result.NewName)}[/]");
                    renamed++;
                    break;

                case RenameStatus.Renamed:
                    AnsiConsole.MarkupLine(
                        $"[green]{Markup.Escape(result.OriginalName)}[/] [dim]→[/] [green]{Markup.Escape(result.NewName)}[/]");
                    renamed++;
                    break;

                case RenameStatus.Skipped:
                    AnsiConsole.MarkupLine(
                        $"[grey]SKIP {Markup.Escape(result.OriginalName)}: {Markup.Escape(result.FailureReason ?? string.Empty)}[/]");
                    skipped++;
                    break;

                case RenameStatus.Failed:
                    AnsiConsole.MarkupLine(
                        $"[red]FAIL {Markup.Escape(result.OriginalName)}: {Markup.Escape(result.FailureReason ?? string.Empty)}[/]");
                    failed++;
                    break;
            }
        }

        if (settings.DryRun)
        {
            AnsiConsole.MarkupLine(
                $"[yellow]Dry run complete — 0 files modified.[/] ({renamed} would rename, {skipped} skipped)");
        }
        else
        {
            AnsiConsole.MarkupLine(
                $"[bold]Done:[/] {renamed} renamed, {skipped} skipped, {failed} failed.");
        }

        return failed > 0 ? 1 : 0;
    }
}
