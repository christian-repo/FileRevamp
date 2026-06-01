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
        var results = orchestrator.Execute(ExportsDir, matcher, dryRun: true).ToList();

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
        var results = orchestrator.Execute(ExportsDir, matcher, dryRun: true).ToList();

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
        var results = orchestrator.Execute(ExportsDir, matcher, dryRun: false).ToList();

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
}
