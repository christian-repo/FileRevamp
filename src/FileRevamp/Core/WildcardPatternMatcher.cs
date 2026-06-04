using System.Text;
using System.Text.RegularExpressions;

namespace FileRevamp.Core;

/// <summary>
/// Applies one or more wildcard remove patterns to a filename string.
/// Patterns are compiled via <see cref="WildcardCompiler"/> at construction time.
///
/// Each pattern is compiled TWICE:
///   - Anchored regex (^...$): used as an IsMatch gate — does this pattern match the whole stem?
///   - Unanchored regex: used for Regex.Replace to strip a matching substring.
///
/// Wildcard patterns (those containing {*}/{+}/{?}) use the BuildRemoveReplacement to
/// preserve the literal prefix and first capture group (stem-level removal).
/// Literal patterns (no wildcards) use empty-string replacement for substring removal.
///
/// NFC normalization is applied to the filename stem before matching (Pitfall 10 mitigation).
/// </summary>
public sealed class WildcardPatternMatcher
{
    private readonly IReadOnlyList<(Regex MatchRegex, Regex RemoveRegex, string Replacement)> _patterns;

    /// <summary>
    /// Returns <see langword="true"/> when at least one remove pattern is registered.
    /// When this is <see langword="false"/>, the orchestrator applies replace transforms
    /// to ALL files in the directory (replace-only mode, no file filtering by pattern).
    /// </summary>
    public bool HasPatterns => _patterns.Count > 0;

    /// <summary>
    /// Initialises the matcher, compiling each pattern via <see cref="WildcardCompiler.ToRegex"/>.
    /// </summary>
    /// <param name="removePatterns">Zero or more wildcard patterns to remove from filenames.</param>
    public WildcardPatternMatcher(IEnumerable<string> removePatterns)
    {
        _patterns = removePatterns
            .Select(p =>
            {
                var matchRegex = WildcardCompiler.ToRegex(p, anchored: true);
                var removeRegex = WildcardCompiler.ToRegex(p, anchored: false);
                var replacement = WildcardCompiler.BuildRemoveReplacement(p);
                return (matchRegex, removeRegex, replacement);
            })
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Applies all remove patterns to the filename in order.
    ///
    /// For wildcard patterns (containing {*}/{+}/{?}): matched against the full stem using
    /// the anchored regex; the replacement string preserves the literal prefix and first capture.
    ///
    /// For literal patterns (no wildcards): the unanchored regex removes the matching substring
    /// from wherever it appears in the stem.
    ///
    /// The extension is always preserved separately so {*} cannot consume the dot separator.
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
        // This preserves the extension through transforms and prevents {*} from
        // consuming the dot-extension separator.
        var extension = Path.GetExtension(normalizedFilename);
        var stem = Path.GetFileNameWithoutExtension(normalizedFilename);

        var currentStem = stem;
        var anyMatch = false;

        foreach (var (matchRegex, removeRegex, replacement) in _patterns)
        {
            // Strategy:
            //   1. Try the anchored match — does the pattern match the WHOLE current stem?
            //      If yes, use the precomputed replacement (keeps literal prefix + $1 capture).
            //   2. If anchored match fails, try unanchored — does it appear anywhere in the stem?
            //      If yes, replace the matched substring with "".
            // This dual approach supports both wildcard patterns (_{*}new_{*}) and
            // literal patterns (_new) without changing the existing wildcard behavior.

            if (matchRegex.IsMatch(currentStem))
            {
                // Wildcard or full-stem-matching pattern: use the computed replacement.
                var result = matchRegex.Replace(currentStem, replacement);

                // WR-02 / CR-03: If the anchored replacement consumed the entire stem and
                // there is an extension, returning the extension alone would create a
                // hidden/inaccessible file (e.g. ".csv"). Treat this as a non-match so the
                // orchestrator skips the file rather than producing an extension-only name.
                if (result.Length == 0 && extension.Length > 0)
                    return null;

                currentStem = result;
                anyMatch = true;
            }
            else if (removeRegex.IsMatch(currentStem))
            {
                // Literal/substring pattern: remove the matching substring.
                var afterRemove = removeRegex.Replace(currentStem, string.Empty);

                // Guard: avoid extension-only result from unanchored removal (mirrors anchored-path guard above).
                // If the removal consumed the entire stem and there is an extension, returning the extension
                // alone would create a hidden/inaccessible file (e.g. ".csv"). Return null so the orchestrator
                // skips the file consistently, regardless of whether the match was anchored or unanchored.
                if (afterRemove.Length == 0 && extension.Length > 0)
                    return null;

                currentStem = afterRemove;
                anyMatch = true;
            }
        }

        if (!anyMatch)
            return null;

        return currentStem + extension;
    }
}
