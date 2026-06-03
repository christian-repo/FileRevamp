using System.ComponentModel;
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

    [CommandOption("--remove")]
    [Description("Wildcard pattern to remove from filenames. Use {*} for any chars, {+} for one or more, {?} for zero or one. Can be specified multiple times.")]
    public string[]? RemovePatterns { get; init; }

    [CommandOption("--replace")]
    [Description("Replace operation in the form old->new (e.g. .->- replaces dots with dashes). Can be specified multiple times. Applied after all --remove operations.")]
    public string[]? ReplaceOperations { get; init; }

    [CommandOption("--dry-run|-n")]
    [Description("Preview renames without modifying any files. Displays a DRY RUN prefix on each line.")]
    public bool DryRun { get; init; }

    /// <summary>
    /// Validates settings before execution. Detects bare unbraced wildcards in remove patterns.
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
            // Detect bare * or ? not enclosed in {} (Pitfall 11 from PITFALLS.md)
            if (HasBareWildcard(pattern))
            {
                return ValidationResult.Error(
                    $"Pattern '{pattern}' uses bare '*' or '?' — did you mean '{{*}}' or '{{?}}'? Use --help for examples.");
            }
        }

        return ValidationResult.Success();
    }

    private static bool HasBareWildcard(string pattern)
    {
        for (var i = 0; i < pattern.Length; i++)
        {
            var ch = pattern[i];
            if (ch == '*' || ch == '?')
            {
                // Check if preceded by '{' and followed by '}'
                var inBraces = i > 0 && pattern[i - 1] == '{' &&
                               i + 1 < pattern.Length && pattern[i + 1] == '}';
                if (!inBraces)
                    return true;
            }
        }
        return false;
    }
}
