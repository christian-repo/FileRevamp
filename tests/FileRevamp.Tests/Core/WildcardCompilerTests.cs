using FileRevamp.Core;
using FluentAssertions;

namespace FileRevamp.Tests.Core;

public class WildcardCompilerTests
{
    // ── Positive match tests ─────────────────────────────────────────────────

    [Theory]
    [InlineData("{*}", "")]
    [InlineData("{*}", "anything_at_all")]
    [InlineData("_{*}new_{*}", "_foo_new_bar")]
    [InlineData("prefix_{*}", "prefix_anything")]
    [InlineData("{?}", "")]
    [InlineData("{?}", "x")]
    [InlineData("data(final)", "data(final)")]
    public void ToRegex_ShouldMatch_WhenPatternMatches(string pattern, string input)
    {
        var regex = WildcardCompiler.ToRegex(pattern);
        regex.IsMatch(input).Should().BeTrue(
            because: $"pattern '{pattern}' should match '{input}'");
    }

    // ── Negative match tests ─────────────────────────────────────────────────

    [Theory]
    [InlineData("_{*}new_{*}", "no_underscore_prefix")]
    [InlineData("report.v2", "reportXv2")]
    [InlineData("{+}", "")]
    [InlineData("prefix_{*}", "XYZprefix_anything")]
    public void ToRegex_ShouldNotMatch_WhenPatternDoesNotMatch(string pattern, string input)
    {
        var regex = WildcardCompiler.ToRegex(pattern);
        regex.IsMatch(input).Should().BeFalse(
            because: $"pattern '{pattern}' should NOT match '{input}'");
    }

    // ── Specific behaviour tests ─────────────────────────────────────────────

    [Fact]
    public void ToRegex_EscapesDot_SoReportV2DoesNotMatchReportXv2()
    {
        var regex = WildcardCompiler.ToRegex("report.v2");
        regex.IsMatch("report.v2").Should().BeTrue();
        regex.IsMatch("reportXv2").Should().BeFalse(
            because: "dot in pattern must be treated as a literal dot, not 'any char'");
    }

    [Fact]
    public void ToRegex_EscapesParens_SoDataFinalMatchesLiterally()
    {
        var regex = WildcardCompiler.ToRegex("data(final)");
        regex.IsMatch("data(final)").Should().BeTrue();
        // '(' and ')' are NOT regex group delimiters — they are literal chars
        regex.IsMatch("datafinal").Should().BeFalse(
            because: "parentheses in pattern must be treated as literals, not group anchors");
    }

    [Fact]
    public void ToRegex_StarToken_MatchesEmptyString()
    {
        var regex = WildcardCompiler.ToRegex("{*}");
        regex.IsMatch("").Should().BeTrue(because: "{*} means zero or more chars");
    }

    [Fact]
    public void ToRegex_PlusToken_DoesNotMatchEmptyString()
    {
        var regex = WildcardCompiler.ToRegex("{+}");
        regex.IsMatch("").Should().BeFalse(because: "{+} means one or more chars");
    }

    [Fact]
    public void ToRegex_QuestionToken_MatchesBothEmptyAndSingleChar()
    {
        var regex = WildcardCompiler.ToRegex("{?}");
        regex.IsMatch("").Should().BeTrue(because: "{?} means zero or one char");
        regex.IsMatch("x").Should().BeTrue(because: "{?} means zero or one char");
        regex.IsMatch("xx").Should().BeFalse(because: "{?} should not match two chars");
    }

    [Fact]
    public void ToRegex_IsAnchored_SoPrefixPatternDoesNotMatchPrecededByOtherChars()
    {
        var regex = WildcardCompiler.ToRegex("prefix_{*}");
        regex.IsMatch("prefix_anything").Should().BeTrue();
        regex.IsMatch("XYZprefix_anything").Should().BeFalse(
            because: "pattern must be anchored to start of string");
    }
}
