using FileRevamp.Core;
using FileRevamp.Tests.Fakes;
using FluentAssertions;

namespace FileRevamp.Tests.Core;

public class CollisionResolverTests
{
    private static readonly string ExportsDir =
        Path.Combine(Path.GetTempPath(), "filerevamp_test_exports");

    private static HashSet<string> NoSources() =>
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Free name scenario: desired name is not on disk and not claimed.
    /// Expected: resolver returns the same name, claimed now contains it.
    /// </summary>
    [Fact]
    public void Resolve_DesiredNameFree_ReturnsSameName_AndAddsToClaimed()
    {
        // Arrange
        var fs = new MockFileSystem(Array.Empty<string>());
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolver = new CollisionResolver(fs, NoSources());

        // Act
        var result = resolver.Resolve(ExportsDir, "report.csv", claimed);

        // Assert
        result.Should().Be("report.csv");
        claimed.Should().Contain("report.csv");
    }

    /// <summary>
    /// Claimed scenario: desired name is already in the claimed set.
    /// Expected: resolver returns "stem(1).ext" and claimed now contains "stem(1).ext".
    /// </summary>
    [Fact]
    public void Resolve_DesiredNameInClaimed_ReturnsNumbered()
    {
        // Arrange
        var fs = new MockFileSystem(Array.Empty<string>());
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "report.csv" };
        var resolver = new CollisionResolver(fs, NoSources());

        // Act
        var result = resolver.Resolve(ExportsDir, "report.csv", claimed);

        // Assert
        result.Should().Be("report(1).csv");
        claimed.Should().Contain("report(1).csv");
    }

    /// <summary>
    /// Disk-exists scenario: desired name not in claimed but file already exists on disk
    /// and is NOT a source file (pre-existing non-source file).
    /// Expected: resolver returns "stem(1).ext".
    /// </summary>
    [Fact]
    public void Resolve_DesiredNameOnDisk_ReturnsNumbered()
    {
        // Arrange — report.csv already exists on disk and is not a source file
        var fs = new MockFileSystem(new[] { Path.Combine(ExportsDir, "report.csv") });
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolver = new CollisionResolver(fs, NoSources());

        // Act
        var result = resolver.Resolve(ExportsDir, "report.csv", claimed);

        // Assert
        result.Should().Be("report(1).csv");
        claimed.Should().Contain("report(1).csv");
    }

    /// <summary>
    /// Two-file collision scenario: two calls with the same desired name against the same claimed set.
    /// Expected: first call returns "report.csv", second returns "report(1).csv"; claimed contains both.
    /// </summary>
    [Fact]
    public void Resolve_TwoFilesComputeSameName_BothGetUniqueNames()
    {
        // Arrange
        var fs = new MockFileSystem(Array.Empty<string>());
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolver = new CollisionResolver(fs, NoSources());

        // Act
        var first = resolver.Resolve(ExportsDir, "report.csv", claimed);
        var second = resolver.Resolve(ExportsDir, "report.csv", claimed);

        // Assert
        first.Should().Be("report.csv");
        second.Should().Be("report(1).csv");
        claimed.Should().Contain("report.csv");
        claimed.Should().Contain("report(1).csv");
    }

    /// <summary>
    /// Slot-one-occupied scenario: "stem(1).ext" already exists on disk → must return "stem(2).ext".
    /// </summary>
    [Fact]
    public void Resolve_SlotOneOccupied_ReturnsTwoSuffix()
    {
        // Arrange — both report.csv and report(1).csv exist on disk and are not source files
        var fs = new MockFileSystem(new[]
        {
            Path.Combine(ExportsDir, "report.csv"),
            Path.Combine(ExportsDir, "report(1).csv")
        });
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolver = new CollisionResolver(fs, NoSources());

        // Act
        var result = resolver.Resolve(ExportsDir, "report.csv", claimed);

        // Assert
        result.Should().Be("report(2).csv");
        claimed.Should().Contain("report(2).csv");
    }

    /// <summary>
    /// No-extension scenario: desired name has no extension → numbered suffix appended directly.
    /// Expected: "readme" → "readme(1)" (no trailing dot).
    /// </summary>
    [Fact]
    public void Resolve_NoExtension_NumberingSuffix()
    {
        // Arrange
        var fs = new MockFileSystem(Array.Empty<string>());
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "readme" };
        var resolver = new CollisionResolver(fs, NoSources());

        // Act
        var result = resolver.Resolve(ExportsDir, "readme", claimed);

        // Assert
        result.Should().Be("readme(1)");
        claimed.Should().Contain("readme(1)");
    }

    /// <summary>
    /// CR-01 regression test: destination name matches a SOURCE file in the batch (will vacate).
    /// The resolver must NOT treat a source file that is about to be renamed away as an occupied slot.
    /// Expected: resolver returns "b.csv" (not "b(1).csv") because b.csv is in sourceNames and will vacate.
    /// </summary>
    [Fact]
    public void Resolve_DestinationMatchesSourceFile_DoesNotFalsePositiveCollide()
    {
        // Arrange — a.csv and b.csv both exist on disk; b.csv is a source file that will vacate.
        // The rename computes: a.csv → b.csv (b.csv will be renamed away in the same batch).
        var fs = new MockFileSystem(new[]
        {
            Path.Combine(ExportsDir, "a.csv"),
            Path.Combine(ExportsDir, "b.csv")
        });
        // sourceNames contains both source files — they will all vacate during Execute.
        var sourceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a.csv", "b.csv" };
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolver = new CollisionResolver(fs, sourceNames);

        // Act: resolve destination "b.csv" for source "a.csv"
        var result = resolver.Resolve(ExportsDir, "b.csv", claimed);

        // Assert: should get "b.csv", not "b(1).csv"
        result.Should().Be("b.csv",
            because: "b.csv is a source file that vacates during Execute and must not block the destination");
        claimed.Should().Contain("b.csv");
        claimed.Should().NotContain("b(1).csv");
    }

    /// <summary>
    /// Source file on disk that is NOT in sourceNames (pre-existing non-source) still blocks destination.
    /// Expected: resolver returns "stem(1).ext" for the non-source pre-existing file.
    /// </summary>
    [Fact]
    public void Resolve_DestinationMatchesDiskFile_NotInSources_ReturnsNumbered()
    {
        // Arrange — c.csv exists on disk but is NOT a source file in this batch.
        var fs = new MockFileSystem(new[] { Path.Combine(ExportsDir, "c.csv") });
        var sourceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a.csv" }; // only a.csv is source
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolver = new CollisionResolver(fs, sourceNames);

        // Act: try to resolve "c.csv" as a destination
        var result = resolver.Resolve(ExportsDir, "c.csv", claimed);

        // Assert: c.csv is occupied by a non-source file → must get numbered
        result.Should().Be("c(1).csv",
            because: "c.csv exists on disk and is not a source file being renamed away");
    }

    /// <summary>
    /// RenameProposal record constructor: all four properties accessible as declared.
    /// </summary>
    [Fact]
    public void RenameProposal_Constructor_AllPropertiesAccessible()
    {
        // Act
        var proposal = new RenameProposal(
            Path.Combine(ExportsDir, "a.csv"), "a.csv", "report.csv", WouldChange: true);

        // Assert
        proposal.SourcePath.Should().Be(Path.Combine(ExportsDir, "a.csv"));
        proposal.OriginalName.Should().Be("a.csv");
        proposal.ResolvedName.Should().Be("report.csv");
        proposal.WouldChange.Should().BeTrue();
    }
}
