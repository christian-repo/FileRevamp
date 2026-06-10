using System.Text;
using System.Text.RegularExpressions;

namespace FileRevamp.Core;

/// <summary>
/// Applies one or more wildcard remove patterns to a filename string.
/// Patterns are compiled via <see cref="WildcardCompiler"/> at construction time into a
/// single unanchored regex each — used to find and strip the matching substring, wherever
/// it occurs in the stem. This is the same "find the matched span, delete it" semantic for
/// BOTH wildcard patterns (e.g. "_{*}new_{*}") and pure literal patterns (e.g. "_new"):
/// a remove pattern always means "delete the text this pattern matches", never "keep part
/// of the matched text" (issue #7 — a previous "keep literal prefix + first capture"
/// replacement scheme produced corrupted results such as "_____.txt").
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
    /// Initialises the matcher, compiling each pattern via <see cref="WildcardCompiler.ToRegex"/>
    /// into an unanchored regex (matches the pattern anywhere in the stem).
    /// </summary>
    /// <param name="removePatterns">Zero or more wildcard patterns to remove from filenames.</param>
    public WildcardPatternMatcher(IEnumerable<string> removePatterns)
    {
        _removeRegexes = removePatterns
            .Select(p => WildcardCompiler.ToRegex(p, anchored: false))
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Applies all remove patterns to the filename in order. Each pattern that matches
    /// anywhere in the current stem has its matched span deleted (replaced with "").
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
