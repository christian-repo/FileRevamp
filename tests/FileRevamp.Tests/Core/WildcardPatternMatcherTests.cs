using System.Text;
using FileRevamp.Core;
using FluentAssertions;

namespace FileRevamp.Tests.Core;

public class WildcardPatternMatcherTests
{
    /// <summary>
    /// ApplyRemoves normalizes NFD filename to NFC before matching a NFC pattern.
    /// A file with NFD encoding must match a pattern compiled from NFC.
    /// The pattern "_cafe_" removes only a prefix, so the stem is not fully erased and a
    /// non-null result is returned (WR-02: full-stem erasure now returns null instead).
    /// </summary>
    [Fact]
    public void ApplyRemoves_NfdFilename_MatchesNfcPattern()
    {
        // NFD "e with combining acute" = U+0065 + U+0301
        var nfdE = "é";
        // The filename uses NFD: "_caf" + NFD_e + "_report.csv"
        var nfdFilename = "_caf" + nfdE + "_report.csv";

        // The pattern uses the NFC form: "_café_" (e with acute as single codepoint U+00E9).
        // After NFC normalization of the filename, both sides use the same codepoint.
        // The pattern "_café_" matches the prefix; "report" survives as the stem.
        var nfcPattern = "_café_"; // NFC e-acute as single codepoint

        var matcher = new WildcardPatternMatcher(new[] { nfcPattern });

        // The stem "_caf<NFC-e>_report" after NFC normalization — the unanchored regex removes
        // "_caf<e>_" and the remaining stem is "report".
        var result = matcher.ApplyRemoves(nfdFilename);

        result.Should().NotBeNull(because: "NFC-normalized filename should match NFC pattern");
        result.Should().Be("report.csv", because: "only the '_cafe_' prefix is removed");
    }

    /// <summary>
    /// ApplyRemoves returns null when no remove patterns are provided.
    /// </summary>
    [Fact]
    public void ApplyRemoves_NoPatterns_ReturnsNull()
    {
        var matcher = new WildcardPatternMatcher(Array.Empty<string>());
        var result = matcher.ApplyRemoves("anyfile.csv");
        result.Should().BeNull(because: "no patterns means nothing to remove");
    }

    /// <summary>
    /// ApplyRemoves with two patterns strips both matching segments from the filename.
    /// Pattern "_new" (literal) removes "_new" from "file_new_name"
    /// Pattern "_name" (literal) removes "_name" from the result
    /// Applied to "file_new_name.txt" -> after "_new" remove -> "file_name.txt" -> after "_name" remove -> "file.txt"
    /// </summary>
    [Fact]
    public void ApplyRemoves_TwoLiteralPatterns_StripsBothSegments()
    {
        var matcher = new WildcardPatternMatcher(new[] { "_new", "_name" });
        var result = matcher.ApplyRemoves("file_new_name.txt");

        // "_new" removed: "file_name.txt"
        // "_name" removed: "file.txt"
        result.Should().Be("file.txt");
    }

    /// <summary>
    /// ApplyRemoves returns null when the anchored match would fully erase the stem (WR-02 / CR-03).
    /// A literal pattern that equals the entire stem would produce an extension-only filename.
    /// </summary>
    [Fact]
    public void ApplyRemoves_FullStemErasure_ReturnsNull()
    {
        // Pattern "_new" exactly matches the stem of "_new.csv"
        var matcher = new WildcardPatternMatcher(new[] { "_new" });
        var result = matcher.ApplyRemoves("_new.csv");

        result.Should().BeNull(because: "fully erasing the stem would produce the extension-only file '.csv'");
    }

    /// Patterns are passed to Regex verbatim — standard .NET regex quantifier and replace
    /// semantics apply. Pattern "_.*?_" lazily matches the shortest "_..._" span, and
    /// <see cref="Regex.Replace(string, string)"/> removes ALL non-overlapping matches, not
    /// just the first. On "a_draft_b_final_c" this matches both "_draft_" and "_final_",
    /// leaving "abc".
    /// </summary>
    [Fact]
    public void ApplyRemoves_RegexLazyQuantifierPattern_RemovesAllNonOverlappingMatches()
    {
        var matcher = new WildcardPatternMatcher(new[] { "_.*?_" });
        var result = matcher.ApplyRemoves("a_draft_b_final_c.txt");

        result.Should().Be("abc.txt");
    }

    /// <summary>
    /// The constructor rejects patterns that are not syntactically valid .NET regular
    /// expressions, throwing ArgumentException with a message naming the offending pattern
    /// (no wildcard translation is performed — patterns are passed to Regex verbatim).
    /// </summary>
    [Fact]
    public void Constructor_InvalidRegexPattern_ThrowsArgumentExceptionWithExplanation()
    {
        var act = () => new WildcardPatternMatcher(new[] { "[unclosed" });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*[unclosed*not a valid regular expression*");
    }
}
