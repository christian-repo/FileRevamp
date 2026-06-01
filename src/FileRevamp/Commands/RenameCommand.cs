using FileRevamp.Core;
using FileRevamp.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FileRevamp.Commands;

/// <summary>
/// Main CLI command: scans a directory and applies remove patterns to filenames.
/// In dry-run mode no files are modified; results are tagged [DRY RUN].
/// </summary>
public sealed class RenameCommand : Command<RenameSettings>
{
    protected override int Execute(CommandContext context, RenameSettings settings, CancellationToken cancellationToken = default)
    {
        // T-01-02: Resolve to absolute path at entry point.
        var directoryPath = System.IO.Path.GetFullPath(settings.Path);

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

        var orchestrator = new RenameOrchestrator(fileSystem);
        var results = orchestrator.Execute(directoryPath, patternMatcher, settings.DryRun);

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
