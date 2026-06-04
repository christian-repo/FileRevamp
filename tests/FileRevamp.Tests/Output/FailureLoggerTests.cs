using FileRevamp.Output;
using FluentAssertions;

namespace FileRevamp.Tests.Output;

/// <summary>
/// Unit tests for FailureLogger — lazy-write append logger for rename failures (RPRT-03).
/// Tests verify: lazy file creation, line format, multiple-call append, and LogFilePath property.
/// </summary>
public sealed class FailureLoggerTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"filerevamp_logger_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    [Fact]
    public void FailureLogger_Log_CreatesFileWithFormattedLine()
    {
        // Arrange
        var tempDir = CreateTempDir();
        var logger = new FailureLogger(tempDir);

        // Act
        logger.Log("report.csv", "Access denied");

        // Assert
        var logPath = Path.Combine(tempDir, "rename-failures.log");
        File.Exists(logPath).Should().BeTrue("log file should be created on first Log() call");
        var content = File.ReadAllText(logPath);
        content.Should().Contain("FAIL report.csv: Access denied");
    }

    [Fact]
    public void FailureLogger_Log_NoCallMade_FileNotCreated()
    {
        // Arrange
        var tempDir = CreateTempDir();
        var logger = new FailureLogger(tempDir);

        // Act — do NOT call Log()

        // Assert
        var logPath = Path.Combine(tempDir, "rename-failures.log");
        File.Exists(logPath).Should().BeFalse("log file must not be created if Log() is never called");
    }

    [Fact]
    public void FailureLogger_Log_MultipleFailures_AllLinesAppended()
    {
        // Arrange
        var tempDir = CreateTempDir();
        var logger = new FailureLogger(tempDir);

        // Act
        logger.Log("report.csv", "Access denied");
        logger.Log("export.xlsx", "File in use");

        // Assert
        var logPath = Path.Combine(tempDir, "rename-failures.log");
        var content = File.ReadAllText(logPath);
        content.Should().Contain("FAIL report.csv: Access denied");
        content.Should().Contain("FAIL export.xlsx: File in use");
    }

    [Fact]
    public void FailureLogger_LogFilePath_ReturnsExpectedPath()
    {
        // Arrange
        var tempDir = CreateTempDir();
        var logger = new FailureLogger(tempDir);

        // Act
        var logFilePath = logger.LogFilePath;

        // Assert
        logFilePath.Should().Be(Path.Combine(tempDir, "rename-failures.log"));
    }

    [Fact]
    public void FailureLogger_Log_LineFormat_ContainsTimestamp_And_FAILPrefix()
    {
        // Arrange
        var tempDir = CreateTempDir();
        var logger = new FailureLogger(tempDir);

        // Act
        logger.Log("data.csv", "Permission error");

        // Assert
        var logPath = Path.Combine(tempDir, "rename-failures.log");
        var content = File.ReadAllText(logPath);
        // Line must match: [YYYY-MM-DD HH:mm:ssZ] FAIL {name}: {reason}
        content.Should().MatchRegex(@"\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}Z\] FAIL data\.csv: Permission error");
    }
}
