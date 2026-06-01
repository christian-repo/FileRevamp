namespace FileRevamp.Core;

/// <summary>
/// A literal string find-and-replace transform applied to a filename.
///
/// This is NOT a regex replace — <see cref="Apply"/> uses
/// <see cref="string.Replace(string, string, StringComparison)"/> with ordinal comparison.
/// All occurrences of <see cref="Find"/> are replaced with <see cref="Replace"/>.
///
/// Replace transforms are applied AFTER all remove operations in the rename pipeline (PAT-03).
/// </summary>
public sealed class ReplaceTransform
{
    /// <summary>The literal string to search for in the filename.</summary>
    public string Find { get; }

    /// <summary>The literal string to substitute in place of <see cref="Find"/>.</summary>
    public string Replace { get; }

    /// <summary>
    /// Initialises a new <see cref="ReplaceTransform"/> with the given find and replace strings.
    /// </summary>
    /// <param name="find">The literal string to find. Must not be null or empty.</param>
    /// <param name="replace">The literal string to replace with (may be empty to delete occurrences).</param>
    public ReplaceTransform(string find, string replace)
    {
        Find = find;
        Replace = replace;
    }

    /// <summary>
    /// Applies the transform to a filename, replacing all occurrences of <see cref="Find"/>
    /// with <see cref="Replace"/> using ordinal (case-sensitive) string comparison.
    /// </summary>
    /// <param name="filename">The filename string to transform.</param>
    /// <returns>
    /// The transformed filename with all occurrences of <see cref="Find"/> replaced.
    /// Returns the original <paramref name="filename"/> unchanged if <see cref="Find"/> is
    /// not present.
    /// </returns>
    public string Apply(string filename) =>
        filename.Replace(Find, Replace, StringComparison.Ordinal);

    /// <summary>
    /// Parses a replace operand in the format <c>old-&gt;new</c> and returns a
    /// <see cref="ReplaceTransform"/> instance.
    ///
    /// The split is performed on the FIRST occurrence of <c>"-&gt;"</c> so that the replacement
    /// string itself may contain the characters <c>-</c> and <c>&gt;</c>.
    /// </summary>
    /// <param name="operand">
    /// The operand string in <c>"old-&gt;new"</c> format. Examples: <c>".->-"</c>, <c>"_ -> -"</c>.
    /// </param>
    /// <returns>A <see cref="ReplaceTransform"/> with the parsed find and replace strings.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="operand"/> does not contain the <c>"-&gt;"</c> separator.
    /// </exception>
    public static ReplaceTransform Parse(string operand)
    {
        const string separator = "->";
        var separatorIndex = operand.IndexOf(separator, StringComparison.Ordinal);

        if (separatorIndex < 0)
        {
            throw new ArgumentException(
                $"Replace operand must be in format 'old->new', e.g. '.->-'. Got: '{operand}'",
                nameof(operand));
        }

        var find = operand[..separatorIndex];
        var replace = operand[(separatorIndex + separator.Length)..];

        return new ReplaceTransform(find, replace);
    }
}
