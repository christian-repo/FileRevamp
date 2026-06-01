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
///   Step 3: Wrap in ^...$ anchors
///   Step 4: Compile with IgnoreCase | Compiled
///
/// Any other order causes silent over-matching or broken patterns.
/// </summary>
public static class WildcardCompiler
{
    /// <summary>
    /// Converts a wildcard pattern to a compiled <see cref="Regex"/> anchored to the full string.
    /// </summary>
    /// <param name="wildcardPattern">
    /// A pattern using {*}, {+}, {?} as wildcards and literal characters otherwise.
    /// </param>
    /// <returns>A compiled, case-insensitive <see cref="Regex"/> that matches the full string.</returns>
    public static Regex ToRegex(string wildcardPattern)
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

        // Step 3: Wrap in anchors so the pattern matches the full string, not a substring.
        var anchored = "^" + substituted + "$";

        // Step 4: Compile for performance; use IgnoreCase for Windows filename matching.
        return new Regex(anchored, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}
