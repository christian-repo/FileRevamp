using System.Text.RegularExpressions;

namespace FileRevamp.Core;

/// <summary>
/// Applies one or more wildcard remove patterns to a filename string.
/// Patterns are compiled via <see cref="WildcardCompiler"/> at construction time.
/// </summary>
public sealed class WildcardPatternMatcher
{
    private readonly IReadOnlyList<(Regex Regex, string Replacement)> _patterns;

    /// <summary>
    /// Initialises the matcher, compiling each pattern via <see cref="WildcardCompiler.ToRegex"/>.
    /// </summary>
    /// <param name="removePatterns">Zero or more wildcard patterns to remove from filenames.</param>
    public WildcardPatternMatcher(IEnumerable<string> removePatterns)
    {
        _patterns = removePatterns
            .Select(p => (WildcardCompiler.ToRegex(p), WildcardCompiler.BuildRemoveReplacement(p)))
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Applies all remove patterns to the filename in order.
    /// Patterns are matched against the full filename; the replacement replaces the
    /// MATCH with the first capture group ($1), preserving what the first {*}/{+}/{?}
    /// wildcard captured. The extension is preserved separately.
    /// </summary>
    /// <param name="filename">The bare filename (no directory path) to transform.</param>
    /// <returns>
    /// The transformed filename if at least one pattern matched and modified the string;
    /// <see langword="null"/> if no pattern matched at all.
    /// </returns>
    public string? ApplyRemoves(string filename)
    {
        // Split off the extension so patterns operate on the stem only.
        // This preserves the extension through transforms and prevents {*} from
        // consuming the dot-extension separator.
        var extension = Path.GetExtension(filename);
        var stem = Path.GetFileNameWithoutExtension(filename);

        var currentStem = stem;
        var anyMatch = false;

        foreach (var (regex, replacement) in _patterns)
        {
            if (regex.IsMatch(currentStem))
            {
                // Apply the computed replacement: preserves the literal prefix and first capture group,
                // removing the middle literal tokens and trailing wildcard capture.
                currentStem = regex.Replace(currentStem, replacement);
                anyMatch = true;
            }
        }

        if (!anyMatch)
            return null;

        return currentStem + extension;
    }
}
