using FileRevamp.Core;
using FileRevamp.Output;
using FluentAssertions;

namespace FileRevamp.Tests.Core;

public class RenameOrchestratorTests
{
    private const string ExportsDir = "/exports";

    /// <summary>
    /// DryRun scenario: file matches pattern, MoveFile must NOT be called.
    /// Expected: Status=DryRun, NewName="_foo_.csv", MoveCallCount=0
    /// </summary>
    [Fact]
    public void Execute_DryRun_MatchingFile_ReturnsDryRunResult_NoMoveCall()
    {
        // Arrange
        var fs = new MockFileSystem(new[] { "/exports/_foo_new_bar.csv" });
        var matcher = new WildcardPatternMatcher(new[] { "_{*}new_{*}" });
        var orchestrator = new RenameOrchestrator(fs);

        // Act
        var results = orchestrator.Execute(ExportsDir, null, matcher, Array.Empty<ReplaceTransform>(), dryRun: true).ToList();

        // Assert
        results.Should().HaveCount(1);
        var result = results[0];
        result.Status.Should().Be(RenameStatus.DryRun);
        result.OriginalName.Should().Be("_foo_new_bar.csv");
        result.NewName.Should().Be("_foo_.csv");
        fs.MoveCallCount.Should().Be(0, because: "dry run must not touch any files");
    }

    /// <summary>
    /// Skipped scenario: file does NOT match pattern.
    /// Expected: Status=Skipped, MoveCallCount=0
    /// </summary>
    [Fact]
    public void Execute_DryRun_NonMatchingFile_ReturnsSkippedResult_NoMoveCall()
    {
        // Arrange
        var fs = new MockFileSystem(new[] { "/exports/report.final.csv" });
        var matcher = new WildcardPatternMatcher(new[] { "_{*}new_{*}" });
        var orchestrator = new RenameOrchestrator(fs);

        // Act
        var results = orchestrator.Execute(ExportsDir, null, matcher, Array.Empty<ReplaceTransform>(), dryRun: true).ToList();

        // Assert
        results.Should().HaveCount(1);
        results[0].Status.Should().Be(RenameStatus.Skipped);
        fs.MoveCallCount.Should().Be(0, because: "no file matched; nothing to move");
    }

    /// <summary>
    /// Live rename scenario: file matches, dryRun=false → MoveFile must be called once.
    /// Expected: Status=Renamed, MoveCallCount=1, source no longer exists, dest exists.
    /// </summary>
    [Fact]
    public void Execute_LiveRun_MatchingFile_ReturnsRenamedResult_MovesFile()
    {
        // Arrange
        var fs = new MockFileSystem(new[] { "/exports/_foo_new_bar.csv" });
        var matcher = new WildcardPatternMatcher(new[] { "_{*}new_{*}" });
        var orchestrator = new RenameOrchestrator(fs);

        // Act
        var results = orchestrator.Execute(ExportsDir, null, matcher, Array.Empty<ReplaceTransform>(), dryRun: false).ToList();

        // Assert
        results.Should().HaveCount(1);
        results[0].Status.Should().Be(RenameStatus.Renamed);
        results[0].OriginalName.Should().Be("_foo_new_bar.csv");
        results[0].NewName.Should().Be("_foo_.csv");
        fs.MoveCallCount.Should().Be(1, because: "live run must rename the file");
        fs.FileExists("/exports/_foo_new_bar.csv").Should().BeFalse(
            because: "original file should no longer exist after rename");
        fs.FileExists("/exports/_foo_.csv").Should().BeTrue(
            because: "renamed file should now exist at destination path");
    }

    // ── New tests added in Plan 02 ─────────────────────────────────────────────────

    /// <summary>
    /// Test A: Remove "_new" (literal) from "file_new_name.csv" → "file_name.csv", DryRun, MoveCallCount=0.
    /// No replace transforms applied.
    /// </summary>
    [Fact]
    public void Execute_RemoveLiteral_DryRun_RemovesSubstringNoMove()
    {
        // Arrange
        var fs = new MockFileSystem(new[] { "/exports/file_new_name.csv" });
        var matcher = new WildcardPatternMatcher(new[] { "_new" });
        var orchestrator = new RenameOrchestrator(fs);

        // Act
        var results = orchestrator.Execute(
            ExportsDir, null, matcher, Array.Empty<ReplaceTransform>(), dryRun: true).ToList();

        // Assert
        results.Should().HaveCount(1);
        var result = results[0];
        result.Status.Should().Be(RenameStatus.DryRun);
        result.OriginalName.Should().Be("file_new_name.csv");
        result.NewName.Should().Be("file_name.csv");
        fs.MoveCallCount.Should().Be(0, because: "dry run must not touch any files");
    }

    /// <summary>
    /// Test B: Remove "_new" then replace ".->-" on "file_new_name.csv" → "file_name-csv", DryRun.
    /// Verifies that removes happen first, then replace applies to the post-remove result.
    /// </summary>
    [Fact]
    public void Execute_RemoveThenReplace_DryRun_AppliesReplaceAfterRemove()
    {
        // Arrange
        var fs = new MockFileSystem(new[] { "/exports/file_new_name.csv" });
        var matcher = new WildcardPatternMatcher(new[] { "_new" });
        var replaces = new List<ReplaceTransform> { ReplaceTransform.Parse(".->-") };
        var orchestrator = new RenameOrchestrator(fs);

        // Act
        var results = orchestrator.Execute(
            ExportsDir, null, matcher, replaces, dryRun: true).ToList();

        // Assert
        results.Should().HaveCount(1);
        var result = results[0];
        result.Status.Should().Be(RenameStatus.DryRun);
        result.OriginalName.Should().Be("file_new_name.csv");
        // After remove "_new": "file_name.csv"
        // After replace "." → "-": "file_name-csv"
        result.NewName.Should().Be("file_name-csv");
        fs.MoveCallCount.Should().Be(0, because: "dry run must not touch any files");
    }

    /// <summary>
    /// Test C (PAT-03 operation order): Remove fires BEFORE replace.
    /// Remove ".new" from "report.new.2024.csv" → "report.2024.csv",
    /// then replace "." with "-" → "report-2024-csv".
    /// If replace had run first, result would be "report-new-2024-csv" — incorrect.
    /// </summary>
    [Fact]
    public void Execute_OperationOrder_RemoveBeforeReplace_ProducesCorrectResult()
    {
        // Arrange
        var fs = new MockFileSystem(new[] { "/exports/report.new.2024.csv" });
        var matcher = new WildcardPatternMatcher(new[] { ".new" });
        var replaces = new List<ReplaceTransform> { ReplaceTransform.Parse(".->-") };
        var orchestrator = new RenameOrchestrator(fs);

        // Act
        var results = orchestrator.Execute(
            ExportsDir, null, matcher, replaces, dryRun: true).ToList();

        // Assert
        results.Should().HaveCount(1);
        var result = results[0];
        result.Status.Should().Be(RenameStatus.DryRun);
        result.OriginalName.Should().Be("report.new.2024.csv");
        // Remove ".new" first: "report.2024.csv"
        // Replace "." → "-": "report-2024-csv"
        // NOT "report-new-2024-csv" (which would mean replace ran first)
        result.NewName.Should().Be("report-2024-csv");
        fs.MoveCallCount.Should().Be(0, because: "dry run must not touch any files");
    }
}
