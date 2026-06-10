using System.ComponentModel;
using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FileRevamp.Commands;

/// <summary>
/// Command-line settings (arguments and options) for the rename command.
/// Spectre.Console.Cli reads these via reflection and auto-generates --help text.
/// </summary>
public sealed class RenameSettings : CommandSettings
{
    [CommandArgument(0, "<path>")]
    [Description("Directory path containing files to rename, or a glob pattern (e.g. *.csv)")]
    public string Path { get; init; } = string.Empty;

    [CommandOption("--remove <regex>")]
    [Description("Regular expression pattern to remove from filenames (raw .NET regex, e.g. _draft_.*?_final). Must be a syntactically valid regex. Can be specified multiple times.")]
    public string[]? RemovePatterns { get; init; }

    [CommandOption("--replace")]
    [Description("Replace operation in the form old->new (e.g. .->- replaces dots with dashes). The find string is case-sensitive. Can be specified multiple times. Applied after all --remove operations.")]
    public string[]? ReplaceOperations { get; init; }

    [CommandOption("--dry-run|-n")]
    [Description("Preview renames without modifying any files. Displays a DRY RUN prefix on each line.")]
    public bool DryRun { get; init; }

    /// <summary>
    /// Validates settings before execution. Ensures each --remove pattern is a syntactically
    /// valid .NET regular expression.
    /// </summary>
    public override ValidationResult Validate()
    {
        // WR-03: Require at least one --remove pattern or --replace operand.
        if ((RemovePatterns is null || RemovePatterns.Length == 0) &&
            (ReplaceOperations is null || ReplaceOperations.Length == 0))
        {
            return ValidationResult.Error(
                "Specify at least one --remove pattern or --replace operand.");
        }

        if (RemovePatterns is null)
            return ValidationResult.Success();

        foreach (var pattern in RemovePatterns)
        {
            try
            {
                _ = new Regex(pattern);
            }
            catch (ArgumentException ex)
            {
                return ValidationResult.Error(
                    $"--remove pattern '{pattern}' is not a valid regular expression: {ex.Message}");
            }
        }

        return ValidationResult.Success();
    }
}
