using FileRevamp.Core;
using FileRevamp.Tests.Fakes;
using FluentAssertions;

namespace FileRevamp.Tests.Core;

public class FileDiscoveryTests
{
    /// <summary>
    /// GetFiles with "*" glob returns all files in the directory.
    /// </summary>
    [Fact]
    public void GetFiles_StarGlob_ReturnsAllFilesInDirectory()
    {
        // Arrange
        var fs = new MockFileSystem(new[] { "/exports/a.csv", "/exports/b.txt" });
        var discovery = new FileDiscovery(fs);

        // Act
        var result = discovery.GetFiles("/exports", "*").ToList();

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain("/exports/a.csv");
        result.Should().Contain("/exports/b.txt");
    }

    /// <summary>
    /// GetFiles with "*.csv" glob returns only .csv files.
    /// </summary>
    [Fact]
    public void GetFiles_CsvGlob_ReturnsOnlyCsvFiles()
    {
        // Arrange
        var fs = new MockFileSystem(new[] { "/exports/a.csv", "/exports/b.txt" });
        var discovery = new FileDiscovery(fs);

        // Act
        var result = discovery.GetFiles("/exports", "*.csv").ToList();

        // Assert
        result.Should().HaveCount(1);
        result.Should().Contain("/exports/a.csv");
        result.Should().NotContain("/exports/b.txt");
    }

    /// <summary>
    /// GetFiles with "report_*" prefix glob returns only matching files.
    /// </summary>
    [Fact]
    public void GetFiles_PrefixGlob_ReturnsOnlyMatchingFiles()
    {
        // Arrange
        var fs = new MockFileSystem(new[] { "/exports/report_jan.csv", "/exports/data.csv" });
        var discovery = new FileDiscovery(fs);

        // Act
        var result = discovery.GetFiles("/exports", "report_*").ToList();

        // Assert
        result.Should().HaveCount(1);
        result.Should().Contain("/exports/report_jan.csv");
        result.Should().NotContain("/exports/data.csv");
    }

    /// <summary>
    /// GetFiles with empty MockFileSystem returns empty list.
    /// </summary>
    [Fact]
    public void GetFiles_EmptyDirectory_ReturnsEmptyList()
    {
        // Arrange
        var fs = new MockFileSystem(Array.Empty<string>());
        var discovery = new FileDiscovery(fs);

        // Act
        var result = discovery.GetFiles("/exports", "*").ToList();

        // Assert
        result.Should().BeEmpty();
    }
}
