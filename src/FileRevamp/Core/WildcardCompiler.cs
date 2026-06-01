using System.Text.RegularExpressions;

namespace FileRevamp.Core;

/// <summary>
/// Translates the FileRevamp wildcard syntax into a compiled <see cref="Regex"/>.
///
/// Wildcard tokens:
///   {*}  →  .* (zero or more characters)
///   {+}  →  .+ (one or more characters)
///   {?}  →  .? (zero or one character)
///   All other characters are treated as literals (regex-escaped).
///
/// CONVERSION ORDER IS STRICT (per PITFALLS.md Pitfall 4):
///   Step 1: Regex.Escape(wildcardPattern)   — escapes . ( ) [ ] + ^ $ | { } etc.
///   Step 2: Replace escaped brace tokens with regex quantifiers
///   Step 3: Optionally wrap in ^...$ anchors
///   Step 4: Compile with IgnoreCase | Compiled
///
/// Any other order causes silent over-matching or broken patterns.
/// </summary>
public static class WildcardCompiler
{
    /// <summary>
    /// Converts a wildcard pattern to a compiled <see cref="Regex"/>.
    /// </summary>
    /// <param name="wildcardPattern">
    /// A pattern using {*}, {+}, {?} as wildcards and literal characters otherwise.
    /// </param>
    /// <param name="anchored">
    /// When <see langword="true"/> (default), wraps the pattern in ^...$ anchors so it
    /// matches the full string. When <see langword="false"/>, no anchors are added and
    /// the pattern matches a substring anywhere in the input — useful for remove operations
    /// where only a portion of the filename should be stripped.
    /// </param>
    /// <returns>A compiled, case-insensitive <see cref="Regex"/>.</returns>
    public static Regex ToRegex(string wildcardPattern, bool anchored = true)
    {
        // Step 1: Escape ALL regex metacharacters in the raw pattern.
        //         Regex.Escape converts { → \{  } → \}  . → \.  + → \+  ( → \(  ) → \)  etc.
        var escaped = Regex.Escape(wildcardPattern);

        // Step 2: Replace the escaped brace-token sequences with regex quantifiers.
        //         After Regex.Escape, the pattern {*} becomes \{\*} (closing } is NOT escaped by Regex.Escape)
        //         Verified: Regex.Escape("{*}") == @"\{\*}"
        var substituted = escaped
            .Replace(@"\{\*}", "(.*)")
            .Replace(@"\{\+}", "(.+)")
            .Replace(@"\{\?}", "(.?)");

        // Step 3: Optionally wrap in anchors so the pattern matches the full string, not a substring.
        var pattern = anchored ? "^" + substituted + "$" : substituted;

        // Step 4: Compile for performance; use IgnoreCase for Windows filename matching.
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    /// <summary>
    /// Builds the replacement string for a remove operation.
    ///
    /// The "remove" semantic keeps the literal prefix (text before the first wildcard token)
    /// and the first capture group ($1, what the first wildcard matched), effectively
    /// removing the literal middle portion and any trailing wildcard match.
    ///
    /// Example: "_{*}new_{*}" → replacement "$1" applied to stem with regex ^_(.*?)new_(.*)$
    ///          requires the literal prefix "_" to be prepended → "_$1"
    /// </summary>
    /// <param name="wildcardPattern">The raw wildcard pattern.</param>
    /// <returns>A regex replacement string that retains only the pre-wildcard literal prefix and the first capture.</returns>
    public static string BuildRemoveReplacement(string wildcardPattern)
    {
        // Find the position of the first wildcard token in the original pattern.
        var firstTokenIndex = wildcardPattern.IndexOf('{');
        if (firstTokenIndex < 0)
        {
            // No wildcard tokens — the pattern is a pure literal; replacing with "" removes everything.
            return string.Empty;
        }

        // The literal prefix is everything before the first '{'.
        var literalPrefix = wildcardPattern[..firstTokenIndex];

        // Escape the literal prefix so it can be safely embedded in a replacement string.
        // (Replacement strings interpret $ specially; escape any $ in the literal prefix.)
        var escapedPrefix = literalPrefix.Replace("$", "$$");

        // The replacement retains the literal prefix and appends $1 (first capture group).
        return escapedPrefix + "$1";
    }
}
