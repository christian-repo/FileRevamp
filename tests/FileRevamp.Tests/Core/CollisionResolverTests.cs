using FileRevamp.Core;
using FileRevamp.Tests.Fakes;
using FluentAssertions;

namespace FileRevamp.Tests.Core;

public class CollisionResolverTests
{
    private static readonly string ExportsDir =
        Path.Combine(Path.GetTempPath(), "filerevamp_test_exports");

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
        var resolver = new CollisionResolver(fs);

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
        var resolver = new CollisionResolver(fs);

        // Act
        var result = resolver.Resolve(ExportsDir, "report.csv", claimed);

        // Assert
        result.Should().Be("report(1).csv");
        claimed.Should().Contain("report(1).csv");
    }

    /// <summary>
    /// Disk-exists scenario: desired name not in claimed but file already exists on disk.
    /// Expected: resolver returns "stem(1).ext".
    /// </summary>
    [Fact]
    public void Resolve_DesiredNameOnDisk_ReturnsNumbered()
    {
        // Arrange — report.csv already exists on disk
        var fs = new MockFileSystem(new[] { Path.Combine(ExportsDir, "report.csv") });
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolver = new CollisionResolver(fs);

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
        var resolver = new CollisionResolver(fs);

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
        // Arrange — both report.csv and report(1).csv exist on disk
        var fs = new MockFileSystem(new[]
        {
            Path.Combine(ExportsDir, "report.csv"),
            Path.Combine(ExportsDir, "report(1).csv")
        });
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolver = new CollisionResolver(fs);

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
        var resolver = new CollisionResolver(fs);

        // Act
        var result = resolver.Resolve(ExportsDir, "readme", claimed);

        // Assert
        result.Should().Be("readme(1)");
        claimed.Should().Contain("readme(1)");
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
