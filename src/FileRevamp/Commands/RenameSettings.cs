using System.ComponentModel;
using System.Text.RegularExpressions;
using FileRevamp.Core;
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

    [CommandOption("--removeBeg <pattern>")]
    [Description("Remove a pattern anchored to the beginning of the filename stem. Accepts raw .NET regex or wildcard shorthand where {*} means one or more of the preceding character (e.g. _{*} removes leading underscores). Case-insensitive. Can be specified multiple times.")]
    public string[]? RemoveBegPatterns { get; init; }

    [CommandOption("--removeEnd <pattern>")]
    [Description("Remove a pattern anchored to the end of the filename stem (before the extension). Accepts raw .NET regex or wildcard shorthand where {*} means one or more of the preceding character (e.g. _{*} removes trailing underscores). Case-insensitive. Can be specified multiple times.")]
    public string[]? RemoveEndPatterns { get; init; }

    [CommandOption("--replace")]
    [Description("Replace operation in the form old->new (e.g. .->- replaces dots with dashes). The find string is case-sensitive. Can be specified multiple times. Applied after all --remove operations.")]
    public string[]? ReplaceOperations { get; init; }

    [CommandOption("--dry-run|-n")]
    [Description("Preview renames without modifying any files. Displays a DRY RUN prefix on each line.")]
    public bool DryRun { get; init; }

    /// <summary>
    /// Validates settings before execution. Ensures each --remove / --removeBeg / --removeEnd
    /// pattern is a syntactically valid .NET regular expression (after wildcard translation).
    /// </summary>
    public override ValidationResult Validate()
    {
        // WR-03: Require at least one operation.
        if ((RemovePatterns is null || RemovePatterns.Length == 0) &&
            (RemoveBegPatterns is null || RemoveBegPatterns.Length == 0) &&
            (RemoveEndPatterns is null || RemoveEndPatterns.Length == 0) &&
            (ReplaceOperations is null || ReplaceOperations.Length == 0))
        {
            return ValidationResult.Error(
                "Specify at least one --remove, --removeBeg, --removeEnd, or --replace operand.");
        }

        foreach (var pattern in RemovePatterns ?? Array.Empty<string>())
        {
            try { _ = new Regex(pattern); }
            catch (ArgumentException ex)
            {
                return ValidationResult.Error(
                    $"--remove pattern '{pattern}' is not a valid regular expression: {ex.Message}");
            }
        }

        foreach (var pattern in RemoveBegPatterns ?? Array.Empty<string>())
        {
            var translated = AnchoredPatternMatcher.TranslateWildcard(pattern);
            try { _ = new Regex($"^(?:{translated})"); }
            catch (ArgumentException ex)
            {
                return ValidationResult.Error(
                    $"--removeBeg pattern '{pattern}' is not a valid pattern: {ex.Message}");
            }
        }

        foreach (var pattern in RemoveEndPatterns ?? Array.Empty<string>())
        {
            var translated = AnchoredPatternMatcher.TranslateWildcard(pattern);
            try { _ = new Regex($"(?:{translated})$"); }
            catch (ArgumentException ex)
            {
                return ValidationResult.Error(
                    $"--removeEnd pattern '{pattern}' is not a valid pattern: {ex.Message}");
            }
        }

        return ValidationResult.Success();
    }
}
