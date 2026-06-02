using FileRevamp.Output;
using FluentAssertions;

namespace FileRevamp.Tests.Output;

/// <summary>
/// Unit tests for Reporter — pure string formatting, no console dependencies.
/// Tests RPRT-01 (per-file output lines) and RPRT-02 (summary counts).
/// </summary>
public class ReporterTests
{
    private readonly Reporter _reporter = new();

    // --- FormatResultLine tests ---

    [Fact]
    public void FormatResultLine_DryRun_StartsWithDryRunPrefix()
    {
        var result = RenameResult.DryRunResult("old.csv", "new.csv");
        var line = _reporter.FormatResultLine(result);
        line.Should().StartWith("[DRY RUN]");
        line.Should().Contain("old.csv");
        line.Should().Contain("new.csv");
    }

    [Fact]
    public void FormatResultLine_Renamed_ContainsFileNames_NoDryRunPrefix()
    {
        var result = RenameResult.RenamedResult("old.csv", "new.csv");
        var line = _reporter.FormatResultLine(result);
        line.Should().Contain("old.csv");
        line.Should().Contain("new.csv");
        line.Should().NotContain("[DRY RUN]");
    }

    [Fact]
    public void FormatResultLine_Skipped_ContainsSkipAndOriginalName()
    {
        var result = RenameResult.SkippedResult("old.csv", "no match");
        var line = _reporter.FormatResultLine(result);
        line.Should().Contain("SKIP");
        line.Should().Contain("old.csv");
    }

    [Fact]
    public void FormatResultLine_Failed_ContainsFailAndOriginalNameAndReason()
    {
        var result = RenameResult.FailedResult("old.csv", "access denied");
        var line = _reporter.FormatResultLine(result);
        line.Should().Contain("FAIL");
        line.Should().Contain("old.csv");
        line.Should().Contain("access denied");
    }

    // --- FormatSummary tests ---

    [Fact]
    public void FormatSummary_WithMixedResults_ShowsCorrectCounts()
    {
        var results = new List<RenameResult>
        {
            RenameResult.RenamedResult("a.csv", "b.csv"),
            RenameResult.RenamedResult("c.csv", "d.csv"),
            RenameResult.RenamedResult("e.csv", "f.csv"),
            RenameResult.FailedResult("g.csv", "error"),
            RenameResult.SkippedResult("h.csv", "no match"),
            RenameResult.SkippedResult("i.csv", "no match"),
        };
        var summary = _reporter.FormatSummary(results);
        summary.Should().Contain("Renamed: 3");
        summary.Should().Contain("Failed: 1");
        summary.Should().Contain("Skipped: 2");
    }

    [Fact]
    public void FormatSummary_EmptyResults_ShowsZeroRenamed()
    {
        var summary = _reporter.FormatSummary(Enumerable.Empty<RenameResult>());
        summary.Should().Contain("Renamed: 0");
    }

    [Fact]
    public void FormatDryRunComplete_NoResults_ShowsZeroCountsAndZeroFilesModified()
    {
        var msg = _reporter.FormatDryRunComplete(Enumerable.Empty<RenameResult>());
        msg.Should().Contain("Dry run complete");
        msg.Should().Contain("0 files modified");
        msg.Should().Contain("0 would be renamed");
    }

    [Fact]
    public void FormatDryRunComplete_WithResults_ShowsCorrectWouldRenameCount()
    {
        var results = new List<RenameResult>
        {
            RenameResult.DryRunResult("a.csv", "b.csv"),
            RenameResult.DryRunResult("c.csv", "d.csv"),
            RenameResult.SkippedResult("e.csv", "no match"),
        };
        var msg = _reporter.FormatDryRunComplete(results);
        msg.Should().Contain("2 would be renamed");
        msg.Should().Contain("1 skipped");
        msg.Should().Contain("0 files modified");
    }

    // --- ValidateOutputName tests ---

    [Fact]
    public void ValidateOutputName_TrailingDot_ReturnsErrorWithTrailingDot()
    {
        var error = Reporter.ValidateOutputName("file.");
        error.Should().NotBeNull();
        error.Should().Contain("trailing dot");
    }

    [Fact]
    public void ValidateOutputName_TrailingSpace_ReturnsErrorWithTrailingSpace()
    {
        var error = Reporter.ValidateOutputName("file ");
        error.Should().NotBeNull();
        error.Should().Contain("trailing space");
    }

    [Fact]
    public void ValidateOutputName_Empty_ReturnsErrorAboutEmpty()
    {
        var error = Reporter.ValidateOutputName("");
        error.Should().NotBeNull();
        error.Should().Contain("empty");
    }

    [Fact]
    public void ValidateOutputName_ValidName_ReturnsNull()
    {
        var error = Reporter.ValidateOutputName("valid.csv");
        error.Should().BeNull();
    }
}
