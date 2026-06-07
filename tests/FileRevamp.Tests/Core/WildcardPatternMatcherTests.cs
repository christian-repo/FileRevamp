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

    /// <summary>
    /// Regression coverage for issue #7: "_{*}new_{*}" must remove exactly the substring its
    /// (lazy, leftmost-first) regex match spans — never corrupt the surrounding text.
    ///
    /// {*} is documented (see RenameSettings --remove help text and WildcardCompilerTests) as a
    /// generic "any characters, zero-or-more, lazy" wildcard — NOT a per-character repetition
    /// specifier. So "_{*}new_{*}" compiles to the unanchored, non-greedy regex "_(.*?)new_(.*?)"
    /// and the leftmost-first match is removed: remove deletes exactly what the pattern
    /// matches, mirroring literal-pattern behaviour (see the corrected ApplyRemoves above).
    ///
    /// Traced expectations (leftmost lazy match per input, replaced with ""):
    ///   "_____new__Hello.txt"   -> match "_____new_" (the run of 5 leading underscores + "new_")
    ///                              leaves "_Hello"        -> "_Hello.txt"
    ///   "_new_Hello.txt"        -> match "_new_"          leaves "Hello"        -> "Hello.txt"
    ///   "_new____Hello.txt"     -> match "_new_" (first "new_" found, lazy stops there)
    ///                              leaves "___Hello"      -> "___Hello.txt"
    ///   "__new__Hello_new_.txt" -> TWO non-overlapping matches ("__new_" then "_Hello_new_")
    ///                              cover the entire stem -> WR-02/CR-03 guard -> null (unchanged)
    ///   "_new-Hello.txt"        -> no "new_" substring exists ("new-" only) -> no match -> null (unchanged)
    /// </summary>
    [Theory]
    [InlineData("_____new__Hello.txt", "_Hello.txt")]
    [InlineData("_new_Hello.txt", "Hello.txt")]
    [InlineData("_new____Hello.txt", "___Hello.txt")]
    [InlineData("__new__Hello_new_.txt", null)]
    [InlineData("_new-Hello.txt", null)]
    public void ApplyRemoves_UnderscoreNewWildcardPattern_RemovesExactlyTheLazyLeftmostMatch(
        string filename, string? expected)
    {
        var matcher = new WildcardPatternMatcher(new[] { "_{*}new_{*}" });
        var result = matcher.ApplyRemoves(filename);

        result.Should().Be(expected,
            because: "remove must delete exactly the substring the lazy, leftmost-first regex match spans, " +
                     "consistent with the documented {*} = 'any chars, zero-or-more, lazy' semantics");
    }
}
