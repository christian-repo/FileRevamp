using System.Text;
using FileRevamp.Core;
using FluentAssertions;

namespace FileRevamp.Tests.Core;

public class WildcardPatternMatcherTests
{
    /// <summary>
    /// ApplyRemoves normalizes NFD filename to NFC before matching a NFC pattern.
    /// A file with NFD "é" (e + combining acute) must match a pattern compiled from NFC "é".
    /// </summary>
    [Fact]
    public void ApplyRemoves_NfdFilename_MatchesNfcPattern()
    {
        // NFD "é" = U+0065 (e) + U+0301 (combining acute accent)
        var nfdE = "é";
        // The filename uses NFD: "caf" + NFD_e + ".csv"
        var nfdFilename = "caf" + nfdE + ".csv";

        // The pattern uses the NFC form: "café" (single codepoint)
        // We expect the NFC-normalized filename to match the literal pattern "café.csv" → NO
        // Actually the plan says: normalize filename to NFC before matching.
        // Pattern is compiled from "café" (NFC), filename stored as NFD.
        // After normalization, both become NFC and the literal match should work.
        // Use a simple literal pattern that matches the full NFC stem "café"
        var nfcPattern = "café"; // NFC "é" as single codepoint

        var matcher = new WildcardPatternMatcher(new[] { nfcPattern });

        // The stem "café" (after NFC normalization of NFD filename) should match the NFC pattern.
        var result = matcher.ApplyRemoves(nfdFilename);

        // Pattern matches the entire stem (anchored), replaces with "" (literal pattern, no wildcards)
        // Result: "" + ".csv" = ".csv"
        result.Should().NotBeNull(because: "NFC-normalized filename should match NFC pattern");
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
    /// Pattern "_{*}a" removes "_fooa" prefix; pattern "b_{*}" removes "b_baz" suffix.
    /// Applied to "_fooa_barb_baz" (stem "_fooa_barb_baz"):
    ///   First pattern "_{*}a" (anchored ^_(.*?)a$) matches? No, stem is "_fooa_barb_baz" not ending in 'a'.
    ///   Use simpler patterns for predictable behavior:
    ///   Pattern "_new" (literal) removes "_new" from "file_new_name"
    ///   Pattern "_name" (literal) removes "_name" from the result
    ///   Applied to "file_new_name.txt" → after "_new" remove → "file_name.txt" → after "_name" remove → "file.txt"
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
}
