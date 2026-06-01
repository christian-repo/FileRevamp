using FileRevamp.Commands;
using FluentAssertions;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Testing;

namespace FileRevamp.Tests.Commands;

/// <summary>
/// In-process CLI tests for RenameCommand via CommandAppTester.
/// Verifies end-to-end behavior: argument parsing, dry-run preview, live rename,
/// bare-asterisk validation, and summary count output.
/// </summary>
public sealed class RenameCommandTests : IDisposable
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

    private static CommandAppTester CreateTester()
    {
        var tester = new CommandAppTester();
        tester.SetDefaultCommand<RenameCommand>("Rename files in a directory");
        tester.Configure(config =>
        {
            config.SetApplicationName("filerevamp");
        });
        return tester;
    }

    private string CreateTempDir(params string[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"filerevamp_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);

        foreach (var file in files)
            File.WriteAllText(Path.Combine(dir, file), string.Empty);

        return dir;
    }

    // Test 1 — --help exits 0 and describes flags
    [Fact]
    public void Help_ExitsZero_AndDescribesFlagsAndSyntax()
    {
        var tester = CreateTester();
        var result = tester.Run("--help");

        result.ExitCode.Should().Be(0);
        result.Output.Should().Contain("--remove");
        result.Output.Should().Contain("--dry-run");
    }

    // Test 2 — dry-run shows [DRY RUN] lines and "Dry run complete" but leaves files untouched
    [Fact]
    public void DryRun_ShowsDryRunLinesAndCompletion_NoFilesModified()
    {
        var tempDir = CreateTempDir("report_new_data.csv");
        var tester = CreateTester();

        var result = tester.Run(tempDir, "--remove", "_new", "--dry-run");

        result.ExitCode.Should().Be(0);
        result.Output.Should().Contain("[DRY RUN]");
        result.Output.Should().Contain("Dry run complete — 0 files modified.");

        // File must still have its original name (dry-run = no modification)
        File.Exists(Path.Combine(tempDir, "report_new_data.csv")).Should().BeTrue();
        File.Exists(Path.Combine(tempDir, "report_data.csv")).Should().BeFalse();
    }

    // Test 3 — live run renames file on disk and shows expected output
    [Fact]
    public void LiveRun_RenamesFileOnDisk_OutputContainsRenameAndSummary()
    {
        var tempDir = CreateTempDir("report_new_data.csv");
        var tester = CreateTester();

        var result = tester.Run(tempDir, "--remove", "_new");

        result.ExitCode.Should().Be(0);
        result.Output.Should().Contain("report_new_data.csv");
        result.Output.Should().Contain("report_data.csv");
        result.Output.Should().Contain("Renamed: 1");
        result.Output.Should().Contain("Failed: 0");

        File.Exists(Path.Combine(tempDir, "report_data.csv")).Should().BeTrue();
        File.Exists(Path.Combine(tempDir, "report_new_data.csv")).Should().BeFalse();
    }

    // Test 4 — bare asterisk in --remove produces targeted validation error
    [Fact]
    public void BareAsterisk_InRemovePattern_ProducesValidationError()
    {
        var tester = CreateTester();

        var result = tester.Run(".", "--remove", "*suffix");

        result.ExitCode.Should().NotBe(0);
        // Error message must mention {*} or "did you mean" to guide the user
        var output = result.Output;
        (output.Contains("{*}") || output.Contains("did you mean")).Should().BeTrue(
            $"Expected output to contain '{{*}}' or 'did you mean', but got: {output}");
    }

    // Test 5 — summary counts reflect actual results (1 renamed, 1 skipped)
    [Fact]
    public void LiveRun_SummaryCounts_ReflectActualResults()
    {
        // file_new.csv matches --remove "_new" → will be renamed to file.csv
        // nodots.txt does not match "_new" → skipped
        var tempDir = CreateTempDir("file_new.csv", "nodots.txt");
        var tester = CreateTester();

        var result = tester.Run(tempDir, "--remove", "_new");

        result.ExitCode.Should().Be(0);
        result.Output.Should().Contain("Renamed: 1");
        result.Output.Should().Contain("Skipped: 1");
    }
}
