using FileRevamp.Core;
using FileRevamp.Output;
using FileRevamp.Tests.Fakes;
using FluentAssertions;

namespace FileRevamp.Tests.Core;

public class RenameOrchestratorTests
{
    private static readonly string ExportsDir =
        Path.Combine(Path.GetTempPath(), "filerevamp_test_exports");

    private static string F(string filename) => Path.Combine(ExportsDir, filename);

    /// <summary>
    /// DryRun scenario: file matches pattern, MoveFile must NOT be called.
    /// </summary>
    [Fact]
    public void Plan_Execute_DryRun_MatchingFile_ReturnsDryRunResult_NoMoveCall()
    {
        var fs = new MockFileSystem(new[] { F("_foo_new_bar.csv") });
        var filePaths = new List<string> { F("_foo_new_bar.csv") };
        var matcher = new WildcardPatternMatcher(new[] { "_.*?new_" });
        var orchestrator = new RenameOrchestrator(fs);

        var (proposals, earlyResults) = orchestrator.Plan(filePaths, matcher, Array.Empty<ReplaceTransform>(), ExportsDir);
        var results = orchestrator.Execute(proposals, ExportsDir, dryRun: true);

        earlyResults.Should().BeEmpty();
        results.Should().HaveCount(1);
        results[0].Status.Should().Be(RenameStatus.DryRun);
        results[0].OriginalName.Should().Be("_foo_new_bar.csv");
        // Raw regex "_.*?new_": lazy match spans "_foo_new_" (stops at first "new_"), leaving "bar".
        results[0].NewName.Should().Be("bar.csv");
        fs.MoveCallCount.Should().Be(0, because: "dry run must not touch any files");
    }

    /// <summary>
    /// Skipped scenario: file does NOT match pattern — ends up in earlyResults, not proposals.
    /// </summary>
    [Fact]
    public void Plan_Execute_DryRun_NonMatchingFile_ReturnsSkippedInEarlyResults()
    {
        var fs = new MockFileSystem(new[] { F("report.final.csv") });
        var filePaths = new List<string> { F("report.final.csv") };
        var matcher = new WildcardPatternMatcher(new[] { "new_.*" });
        var orchestrator = new RenameOrchestrator(fs);

        var (proposals, earlyResults) = orchestrator.Plan(filePaths, matcher, Array.Empty<ReplaceTransform>(), ExportsDir);

        proposals.Should().BeEmpty(because: "non-matching file never becomes a proposal");
        earlyResults.Should().HaveCount(1);
        earlyResults[0].Status.Should().Be(RenameStatus.Skipped);
        fs.MoveCallCount.Should().Be(0);
    }

    /// <summary>
    /// Live rename scenario: file matches, dryRun=false → MoveFile must be called once.
    /// </summary>
    [Fact]
    public void Plan_Execute_LiveRename_MatchingFile_CallsMoveOnce()
    {
        var fs = new MockFileSystem(new[] { F("_foo_new_bar.csv") });
        var filePaths = new List<string> { F("_foo_new_bar.csv") };
        var matcher = new WildcardPatternMatcher(new[] { "_.*?new_" });
        var orchestrator = new RenameOrchestrator(fs);

        var (proposals, _) = orchestrator.Plan(filePaths, matcher, Array.Empty<ReplaceTransform>(), ExportsDir);
        var results = orchestrator.Execute(proposals, ExportsDir, dryRun: false);

        results.Should().HaveCount(1);
        results[0].Status.Should().Be(RenameStatus.Renamed);
        results[0].OriginalName.Should().Be("_foo_new_bar.csv");
        // Raw regex "_.*?new_": lazy match spans "_foo_new_", leaving "bar".
        results[0].NewName.Should().Be("bar.csv");
        fs.MoveCallCount.Should().Be(1, because: "live run must rename the file");
        fs.FileExists(F("_foo_new_bar.csv")).Should().BeFalse(because: "original must no longer exist");
        fs.FileExists(F("bar.csv")).Should().BeTrue(because: "renamed file must exist at destination");
    }

    /// <summary>
    /// Remove literal "_new" from "file_new_name.csv" → "file_name.csv", DryRun.
    /// </summary>
    [Fact]
    public void Plan_Execute_RemoveLiteral_DryRun_RemovesSubstringNoMove()
    {
        var fs = new MockFileSystem(new[] { F("file_new_name.csv") });
        var filePaths = new List<string> { F("file_new_name.csv") };
        var matcher = new WildcardPatternMatcher(new[] { "_new" });
        var orchestrator = new RenameOrchestrator(fs);

        var (proposals, _) = orchestrator.Plan(filePaths, matcher, Array.Empty<ReplaceTransform>(), ExportsDir);
        var results = orchestrator.Execute(proposals, ExportsDir, dryRun: true);

        results.Should().HaveCount(1);
        results[0].Status.Should().Be(RenameStatus.DryRun);
        results[0].OriginalName.Should().Be("file_new_name.csv");
        results[0].NewName.Should().Be("file_name.csv");
        fs.MoveCallCount.Should().Be(0, because: "dry run must not touch any files");
    }

    /// <summary>
    /// Remove "_new" then replace ".->-" on "file_new_name.csv" → "file_name-csv", DryRun.
    /// Verifies removes happen first, then replace applies to the post-remove result.
    /// </summary>
    [Fact]
    public void Plan_Execute_RemoveThenReplace_DryRun_AppliesReplaceAfterRemove()
    {
        var fs = new MockFileSystem(new[] { F("file_new_name.csv") });
        var filePaths = new List<string> { F("file_new_name.csv") };
        var matcher = new WildcardPatternMatcher(new[] { "_new" });
        var replaces = new List<ReplaceTransform> { ReplaceTransform.Parse(".->-") };
        var orchestrator = new RenameOrchestrator(fs);

        var (proposals, _) = orchestrator.Plan(filePaths, matcher, replaces, ExportsDir);
        var results = orchestrator.Execute(proposals, ExportsDir, dryRun: true);

        results.Should().HaveCount(1);
        results[0].Status.Should().Be(RenameStatus.DryRun);
        results[0].OriginalName.Should().Be("file_new_name.csv");
        // After remove "_new": "file_name.csv"  →  after replace "."->"- ": "file_name-csv"
        results[0].NewName.Should().Be("file_name-csv");
        fs.MoveCallCount.Should().Be(0, because: "dry run must not touch any files");
    }

    /// <summary>
    /// PAT-03 operation order: removes fire BEFORE replaces.
    /// Remove ".new" from "report.new.2024.csv" → "report.2024.csv",
    /// then replace "." → "-" → "report-2024-csv".
    /// </summary>
    [Fact]
    public void Plan_Execute_OperationOrder_RemovesThenReplaces()
    {
        var fs = new MockFileSystem(new[] { F("report.new.2024.csv") });
        var filePaths = new List<string> { F("report.new.2024.csv") };
        var matcher = new WildcardPatternMatcher(new[] { ".new" });
        var replaces = new List<ReplaceTransform> { ReplaceTransform.Parse(".->-") };
        var orchestrator = new RenameOrchestrator(fs);

        var (proposals, _) = orchestrator.Plan(filePaths, matcher, replaces, ExportsDir);
        var results = orchestrator.Execute(proposals, ExportsDir, dryRun: true);

        results.Should().HaveCount(1);
        results[0].Status.Should().Be(RenameStatus.DryRun);
        results[0].OriginalName.Should().Be("report.new.2024.csv");
        // Remove ".new" first → "report.2024.csv"  →  replace "." → "-" → "report-2024-csv"
        results[0].NewName.Should().Be("report-2024-csv");
    }

    // ── Phase 2 collision tests ───────────────────────────────────────────────

    /// <summary>
    /// Two files that both transform to "report.csv" — first gets "report.csv",
    /// second gets auto-numbered "report(1).csv". Both appear in proposals.
    /// </summary>
    [Fact]
    public void Plan_TwoFilesComputeSameName_BothProposals_SecondHasNumberedName()
    {
        var fs = new MockFileSystem(new[] { F("prefix_report.csv"), F("suffix_report.csv") });
        var filePaths = new List<string> { F("prefix_report.csv"), F("suffix_report.csv") };
        var matcher = new WildcardPatternMatcher(Array.Empty<string>());
        var replaces = new List<ReplaceTransform>
        {
            ReplaceTransform.Parse("prefix_->"),
            ReplaceTransform.Parse("suffix_->")
        };
        var orchestrator = new RenameOrchestrator(fs);

        var (proposals, earlyResults) = orchestrator.Plan(filePaths, matcher, replaces, ExportsDir);

        earlyResults.Should().BeEmpty();
        proposals.Should().HaveCount(2);
        proposals[0].ResolvedName.Should().Be("report.csv",
            because: "first file gets the desired name when free");
        proposals[1].ResolvedName.Should().Be("report(1).csv",
            because: "second file gets an auto-numbered name when the desired name is already claimed");
    }

    /// <summary>
    /// Dry-run with two colliding files — both resolved names appear in output,
    /// one base and one auto-numbered. No files are moved.
    /// </summary>
    [Fact]
    public void Plan_TwoFilesComputeSameName_DryRun_BothShowResolvedNames()
    {
        var fs = new MockFileSystem(new[] { F("prefix_report.csv"), F("suffix_report.csv") });
        var filePaths = new List<string> { F("prefix_report.csv"), F("suffix_report.csv") };
        var matcher = new WildcardPatternMatcher(Array.Empty<string>());
        var replaces = new List<ReplaceTransform>
        {
            ReplaceTransform.Parse("prefix_->"),
            ReplaceTransform.Parse("suffix_->")
        };
        var orchestrator = new RenameOrchestrator(fs);

        var (proposals, earlyResults) = orchestrator.Plan(filePaths, matcher, replaces, ExportsDir);
        var results = orchestrator.Execute(proposals, ExportsDir, dryRun: true);

        earlyResults.Should().BeEmpty();
        results.Should().HaveCount(2);
        results.Should().Contain(r => r.NewName == "report.csv" && r.Status == RenameStatus.DryRun);
        results.Should().Contain(r => r.NewName == "report(1).csv" && r.Status == RenameStatus.DryRun);
        fs.MoveCallCount.Should().Be(0, because: "dry run must not touch any files");
    }

    /// <summary>
    /// Verifies the two-pass contract: Plan() must not call MoveFile; only Execute() does.
    /// After Plan(): MoveCallCount=0. After Execute(): MoveCallCount=2 (both proposals executed).
    /// </summary>
    [Fact]
    public void Execute_NoMoveCalledBeforePlanCompletes()
    {
        var fs = new MockFileSystem(new[] { F("prefix_report.csv"), F("suffix_report.csv") });
        var filePaths = new List<string> { F("prefix_report.csv"), F("suffix_report.csv") };
        var matcher = new WildcardPatternMatcher(Array.Empty<string>());
        var replaces = new List<ReplaceTransform>
        {
            ReplaceTransform.Parse("prefix_->"),
            ReplaceTransform.Parse("suffix_->")
        };
        var orchestrator = new RenameOrchestrator(fs);

        var (proposals, _) = orchestrator.Plan(filePaths, matcher, replaces, ExportsDir);

        fs.MoveCallCount.Should().Be(0, because: "Plan() must not touch the file system");

        orchestrator.Execute(proposals, ExportsDir, dryRun: false);

        fs.MoveCallCount.Should().Be(2, because: "Execute() must rename all planned proposals");
    }
}
