using System.Text;
using System.Text.RegularExpressions;

namespace FileRevamp.Core;

/// <summary>
/// Applies user-supplied patterns anchored to the beginning or end of a filename stem.
///
/// Pattern syntax supports two forms:
///   • Wildcard shorthand: <c>{*}</c> is replaced with <c>+</c> (one or more of the preceding
///     character). E.g. <c>_{*}</c> → regex <c>_+</c>; <c>_{*}new_{*}</c> → regex <c>_+new_+</c>.
///   • Raw .NET regex: any other valid regex syntax is passed through verbatim.
///
/// Beg patterns are anchored with <c>^</c>; end patterns are anchored with <c>$</c>.
/// Matching is always case-insensitive.
/// </summary>
public sealed class AnchoredPatternMatcher
{
    private readonly IReadOnlyList<Regex> _begRegexes;
    private readonly IReadOnlyList<Regex> _endRegexes;

    public bool HasBegPatterns => _begRegexes.Count > 0;
    public bool HasEndPatterns => _endRegexes.Count > 0;

    /// <param name="begPatterns">Patterns to remove from the start of the stem.</param>
    /// <param name="endPatterns">Patterns to remove from the end of the stem.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when any pattern (after <c>{*}</c> translation) is not a valid .NET regular expression.
    /// </exception>
    public AnchoredPatternMatcher(
        IEnumerable<string> begPatterns,
        IEnumerable<string> endPatterns)
    {
        _begRegexes = CompileAnchored(begPatterns, anchorStart: true);
        _endRegexes = CompileAnchored(endPatterns, anchorStart: false);
    }

    /// <summary>
    /// Translates <c>{*}</c> wildcard tokens to <c>+</c> quantifiers and compiles each
    /// pattern with a start or end anchor.
    /// </summary>
    private static IReadOnlyList<Regex> CompileAnchored(
        IEnumerable<string> patterns, bool anchorStart)
    {
        var compiled = new List<Regex>();
        foreach (var pattern in patterns)
        {
            var translated = TranslateWildcard(pattern);
            var anchored = anchorStart ? $"^(?:{translated})" : $"(?:{translated})$";
            try
            {
                compiled.Add(new Regex(anchored, RegexOptions.IgnoreCase | RegexOptions.Compiled));
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException(
                    $"'{pattern}' is not a valid pattern: {ex.Message}", ex);
            }
        }
        return compiled.AsReadOnly();
    }

    /// <summary>Replaces <c>{*}</c> with <c>+</c>; all other characters are left as-is.</summary>
    internal static string TranslateWildcard(string pattern) =>
        pattern.Replace("{*}", "+");

    /// <summary>
    /// Applies all beginning-anchored patterns to the stem of <paramref name="filename"/> in order.
    /// Returns the transformed filename, or the original filename if no pattern matched,
    /// or <see langword="null"/> if a match would fully erase the stem (WR-02 / CR-03).
    /// </summary>
    public string? ApplyBegRemoves(string filename) =>
        ApplyPatterns(filename, _begRegexes);

    /// <summary>
    /// Applies all end-anchored patterns to the stem of <paramref name="filename"/> in order.
    /// Returns the transformed filename, or the original filename if no pattern matched,
    /// or <see langword="null"/> if a match would fully erase the stem (WR-02 / CR-03).
    /// </summary>
    public string? ApplyEndRemoves(string filename) =>
        ApplyPatterns(filename, _endRegexes);

    private static string? ApplyPatterns(string filename, IReadOnlyList<Regex> regexes)
    {
        if (regexes.Count == 0)
            return filename;

        var normalizedFilename = filename.Normalize(NormalizationForm.FormC);
        var extension = Path.GetExtension(normalizedFilename);
        var stem = Path.GetFileNameWithoutExtension(normalizedFilename);

        var currentStem = stem;
        var anyMatch = false;

        foreach (var regex in regexes)
        {
            if (!regex.IsMatch(currentStem))
                continue;

            var afterRemove = regex.Replace(currentStem, string.Empty);

            // WR-02 / CR-03: Skip if the removal would leave only the extension.
            if (afterRemove.Length == 0 && extension.Length > 0)
                return null;

            currentStem = afterRemove;
            anyMatch = true;
        }

        return anyMatch ? currentStem + extension : filename;
    }
}
