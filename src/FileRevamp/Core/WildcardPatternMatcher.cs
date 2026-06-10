using System.Text;
using System.Text.RegularExpressions;

namespace FileRevamp.Core;

/// <summary>
/// Applies one or more user-supplied regex remove patterns to a filename string.
///
/// Patterns are passed through verbatim to <see cref="Regex"/> — no wildcard translation
/// or escaping is performed. Each pattern must be a syntactically valid .NET regular
/// expression; the constructor throws <see cref="ArgumentException"/> with an explanatory
/// message for any pattern that fails to compile.
///
/// NFC normalization is applied to the filename stem before matching (Pitfall 10 mitigation).
/// </summary>
public sealed class WildcardPatternMatcher
{
    private readonly IReadOnlyList<Regex> _removeRegexes;

    /// <summary>
    /// Returns <see langword="true"/> when at least one remove pattern is registered.
    /// When this is <see langword="false"/>, the orchestrator applies replace transforms
    /// to ALL files in the directory (replace-only mode, no file filtering by pattern).
    /// </summary>
    public bool HasPatterns => _removeRegexes.Count > 0;

    /// <summary>
    /// Compiles each pattern as a raw .NET regular expression.
    /// </summary>
    /// <param name="removePatterns">Zero or more regex patterns to remove from filenames.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when a pattern is not a syntactically valid .NET regular expression. The message
    /// names the offending pattern and includes the underlying parse error.
    /// </exception>
    public WildcardPatternMatcher(IEnumerable<string> removePatterns)
    {
        var compiled = new List<Regex>();
        foreach (var pattern in removePatterns)
        {
            try
            {
                compiled.Add(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled));
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException(
                    $"'{pattern}' is not a valid regular expression: {ex.Message}", ex);
            }
        }

        _removeRegexes = compiled.AsReadOnly();
    }

    /// <summary>
    /// Applies all remove patterns to the filename in order, removing the first matching
    /// substring each pattern finds in the current stem.
    ///
    /// The extension is always preserved separately so a pattern cannot consume the dot separator.
    ///
    /// Filenames are NFC-normalized before matching to handle NFD filenames from macOS shares
    /// (Pitfall 10 mitigation).
    /// </summary>
    /// <param name="filename">The bare filename (no directory path) to transform.</param>
    /// <returns>
    /// The transformed filename if at least one pattern matched and modified the string;
    /// <see langword="null"/> if no pattern matched at all (or no patterns are registered).
    /// </returns>
    public string? ApplyRemoves(string filename)
    {
        // NFC normalization: filenames from macOS shares may be NFD; patterns from terminal are NFC.
        // Normalize to NFC before matching so both sides use the same codepoint representation (Pitfall 10).
        var normalizedFilename = filename.Normalize(NormalizationForm.FormC);

        // Split off the extension so patterns operate on the stem only.
        // This preserves the extension through transforms and prevents a pattern from
        // consuming the dot-extension separator.
        var extension = Path.GetExtension(normalizedFilename);
        var stem = Path.GetFileNameWithoutExtension(normalizedFilename);

        var currentStem = stem;
        var anyMatch = false;

        foreach (var removeRegex in _removeRegexes)
        {
            if (!removeRegex.IsMatch(currentStem))
                continue;

            var afterRemove = removeRegex.Replace(currentStem, string.Empty);

            // WR-02 / CR-03: If the removal consumed the entire stem and there is an
            // extension, returning the extension alone would create a hidden/inaccessible
            // file (e.g. ".csv"). Treat this as a non-match so the orchestrator skips the
            // file rather than producing an extension-only name.
            if (afterRemove.Length == 0 && extension.Length > 0)
                return null;

            currentStem = afterRemove;
            anyMatch = true;
        }

        if (!anyMatch)
            return null;

        return currentStem + extension;
    }
}
