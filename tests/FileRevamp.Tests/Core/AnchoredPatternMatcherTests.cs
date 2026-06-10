using FileRevamp.Core;
using FluentAssertions;

namespace FileRevamp.Tests.Core;

public class AnchoredPatternMatcherTests
{
    // ── TranslateWildcard ─────────────────────────────────────────────────────

    [Fact]
    public void TranslateWildcard_SingleToken_ReplacesWithPlus()
    {
        AnchoredPatternMatcher.TranslateWildcard("_{*}").Should().Be("_+");
    }

    [Fact]
    public void TranslateWildcard_MultipleTokens_ReplacesAll()
    {
        AnchoredPatternMatcher.TranslateWildcard("_{*}new_{*}").Should().Be("_+new_+");
    }

    [Fact]
    public void TranslateWildcard_NoToken_ReturnedUnchanged()
    {
        AnchoredPatternMatcher.TranslateWildcard("_new").Should().Be("_new");
    }

    [Fact]
    public void TranslateWildcard_RawRegex_ReturnedUnchanged()
    {
        // {4} is a regex quantifier — not {*}, so it must not be touched.
        AnchoredPatternMatcher.TranslateWildcard(@"\d{4}").Should().Be(@"\d{4}");
    }

    // ── ApplyBegRemoves ───────────────────────────────────────────────────────

    [Fact]
    public void ApplyBegRemoves_NoPatterns_ReturnsFilenameUnchanged()
    {
        var matcher = new AnchoredPatternMatcher(Array.Empty<string>(), Array.Empty<string>());
        matcher.ApplyBegRemoves("_foo_bar.csv").Should().Be("_foo_bar.csv");
    }

    [Fact]
    public void ApplyBegRemoves_WildcardToken_RemovesLeadingUnderscores()
    {
        // "_{*}" → "^(?:_+)" — matches and removes leading underscores.
        var matcher = new AnchoredPatternMatcher(new[] { "_{*}" }, Array.Empty<string>());
        matcher.ApplyBegRemoves("___report.csv").Should().Be("report.csv");
    }

    [Fact]
    public void ApplyBegRemoves_WildcardPlusLiteral_RemovesLeadingUnderscoresAndWord()
    {
        // "_{*}new" → "^(?:_+new)" — matches "_new" prefix.
        var matcher = new AnchoredPatternMatcher(new[] { "_{*}new" }, Array.Empty<string>());
        matcher.ApplyBegRemoves("_new_bar.csv").Should().Be("_bar.csv");
    }

    [Fact]
    public void ApplyBegRemoves_MultiSegmentWildcard_RemovesEntirePrefix()
    {
        // "_{*}new_{*}" → "^(?:_+new_+)" — matches "_foo_new__" prefix.
        var matcher = new AnchoredPatternMatcher(new[] { "_{*}new_{*}" }, Array.Empty<string>());
        matcher.ApplyBegRemoves("_new__bar.csv").Should().Be("bar.csv");
    }

    [Fact]
    public void ApplyBegRemoves_LiteralPattern_RemovesExactPrefix()
    {
        var matcher = new AnchoredPatternMatcher(new[] { "_draft" }, Array.Empty<string>());
        matcher.ApplyBegRemoves("_draftreport.csv").Should().Be("report.csv");
    }

    [Fact]
    public void ApplyBegRemoves_RawRegex_RemovesDigitPrefix()
    {
        // Raw regex "\d{4}_" anchored to start — removes a 4-digit year prefix.
        var matcher = new AnchoredPatternMatcher(new[] { @"\d{4}_" }, Array.Empty<string>());
        matcher.ApplyBegRemoves("2024_report.csv").Should().Be("report.csv");
    }

    [Fact]
    public void ApplyBegRemoves_PatternNotAtBeginning_ReturnsFilenameUnchanged()
    {
        // Pattern anchored to start — must not match in the middle.
        var matcher = new AnchoredPatternMatcher(new[] { "_new" }, Array.Empty<string>());
        matcher.ApplyBegRemoves("report_new_final.csv").Should().Be("report_new_final.csv");
    }

    [Fact]
    public void ApplyBegRemoves_FullStemErasure_ReturnsNull()
    {
        // Pattern matches the entire stem — returns null to signal skip (WR-02 / CR-03).
        var matcher = new AnchoredPatternMatcher(new[] { "_new" }, Array.Empty<string>());
        matcher.ApplyBegRemoves("_new.csv").Should().BeNull(
            because: "removing the entire stem would produce extension-only '.csv'");
    }

    [Fact]
    public void ApplyBegRemoves_CaseInsensitive_MatchesUppercase()
    {
        var matcher = new AnchoredPatternMatcher(new[] { "draft_" }, Array.Empty<string>());
        matcher.ApplyBegRemoves("DRAFT_report.csv").Should().Be("report.csv");
    }

    // ── ApplyEndRemoves ───────────────────────────────────────────────────────

    [Fact]
    public void ApplyEndRemoves_NoPatterns_ReturnsFilenameUnchanged()
    {
        var matcher = new AnchoredPatternMatcher(Array.Empty<string>(), Array.Empty<string>());
        matcher.ApplyEndRemoves("report_foo.csv").Should().Be("report_foo.csv");
    }

    [Fact]
    public void ApplyEndRemoves_WildcardToken_RemovesTrailingUnderscores()
    {
        // "_{*}" → "(?:_+)$" — matches and removes trailing underscores.
        var matcher = new AnchoredPatternMatcher(Array.Empty<string>(), new[] { "_{*}" });
        matcher.ApplyEndRemoves("report___.csv").Should().Be("report.csv");
    }

    [Fact]
    public void ApplyEndRemoves_LiteralPattern_RemovesExactSuffix()
    {
        var matcher = new AnchoredPatternMatcher(Array.Empty<string>(), new[] { "_final" });
        matcher.ApplyEndRemoves("report_final.csv").Should().Be("report.csv");
    }

    [Fact]
    public void ApplyEndRemoves_WildcardPlusLiteral_RemovesWordAndTrailingUnderscores()
    {
        // "_new_{*}" → "(?:_new_+)$"
        var matcher = new AnchoredPatternMatcher(Array.Empty<string>(), new[] { "_new_{*}" });
        matcher.ApplyEndRemoves("report_new__.csv").Should().Be("report.csv");
    }

    [Fact]
    public void ApplyEndRemoves_RawRegex_RemovesDateSuffix()
    {
        // Raw regex "_\d{4}" anchored to end.
        var matcher = new AnchoredPatternMatcher(Array.Empty<string>(), new[] { @"_\d{4}" });
        matcher.ApplyEndRemoves("report_2024.csv").Should().Be("report.csv");
    }

    [Fact]
    public void ApplyEndRemoves_PatternNotAtEnd_ReturnsFilenameUnchanged()
    {
        var matcher = new AnchoredPatternMatcher(Array.Empty<string>(), new[] { "_final" });
        matcher.ApplyEndRemoves("_final_report.csv").Should().Be("_final_report.csv");
    }

    [Fact]
    public void ApplyEndRemoves_FullStemErasure_ReturnsNull()
    {
        var matcher = new AnchoredPatternMatcher(Array.Empty<string>(), new[] { "report" });
        matcher.ApplyEndRemoves("report.csv").Should().BeNull(
            because: "removing the entire stem would produce extension-only '.csv'");
    }

    [Fact]
    public void ApplyEndRemoves_CaseInsensitive_MatchesUppercase()
    {
        var matcher = new AnchoredPatternMatcher(Array.Empty<string>(), new[] { "_FINAL" });
        matcher.ApplyEndRemoves("report_final.csv").Should().Be("report.csv");
    }

    // ── Constructor validation ────────────────────────────────────────────────

    [Fact]
    public void Constructor_InvalidBegPattern_ThrowsArgumentException()
    {
        var act = () => new AnchoredPatternMatcher(new[] { "[unclosed" }, Array.Empty<string>());
        act.Should().Throw<ArgumentException>().WithMessage("*[unclosed*");
    }

    [Fact]
    public void Constructor_InvalidEndPattern_ThrowsArgumentException()
    {
        var act = () => new AnchoredPatternMatcher(Array.Empty<string>(), new[] { "[bad" });
        act.Should().Throw<ArgumentException>().WithMessage("*[bad*");
    }

    // ── Multiple patterns applied in order ────────────────────────────────────

    [Fact]
    public void ApplyBegRemoves_TwoPatterns_BothApplied()
    {
        // Two beg patterns: first removes leading underscores, second removes "new_".
        var matcher = new AnchoredPatternMatcher(
            new[] { "_{*}", "new_" }, Array.Empty<string>());
        // "___new_report.csv" → remove "___" → "new_report.csv" → remove "new_" → "report.csv"
        matcher.ApplyBegRemoves("___new_report.csv").Should().Be("report.csv");
    }

    [Fact]
    public void ApplyEndRemoves_TwoPatterns_BothApplied()
    {
        var matcher = new AnchoredPatternMatcher(
            Array.Empty<string>(), new[] { "_draft", "_{*}" });
        // "report__draft.csv" → remove "_draft" → "report_.csv" → remove "_" → "report.csv"
        matcher.ApplyEndRemoves("report__draft.csv").Should().Be("report.csv");
    }
}
