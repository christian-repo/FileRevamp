using FileRevamp.Commands;
using FluentAssertions;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Testing;

namespace FileRevamp.Tests.Commands;

/// <summary>
/// Integration tests for edge cases in RenameCommand via CommandAppTester.
/// Covers all D-02 scenarios: literal dots/parens, batch collision live run,
/// log-file exclusion, empty directory, unicode filenames, and long filenames.
/// All tests use real disk I/O via temp directories (no MockFileSystem injection).
/// </summary>
public sealed class EdgeCaseIntegrationTests : IDisposable
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

    /// <summary>
    /// Creates a CommandAppTester with IAnsiConsole wired to the tester's TestConsole.
    /// This allows CommandAppTester to capture all output written by RenameCommand.
    /// Registers both IAnsiConsole and RenameCommand so Spectre's DI can instantiate
    /// the command with the test console injected.
    /// </summary>
    private static CommandAppTester CreateTester()
    {
        var tester = new CommandAppTester();

        var registrar = new FakeTypeRegistrar();
        registrar.RegisterInstance(typeof(IAnsiConsole), tester.Console);
        registrar.RegisterInstance(typeof(RenameCommand), new RenameCommand(tester.Console));
        tester.Registrar = registrar;

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

    // Test 1 — Wildcard pattern with literal dots and parentheses renames the file correctly (no regex error)
    [Fact]
    public void LiteralDotsAndParensInPattern_RenamesCorrectly()
    {
        // ".(2024)" contains a literal dot and parentheses that must not be treated as regex metacharacters.
        // WildcardCompiler runs Regex.Escape FIRST (Step 1) so . becomes \. and ( ) become \( \).
        var tempDir = CreateTempDir("report.new.(2024).csv");
        var tester = CreateTester();

        var result = tester.Run(tempDir, "--remove", ".(2024)");

        result.ExitCode.Should().Be(0, because: "literal dots and parens in the pattern must not cause a regex error");
        result.Output.Should().Contain("Renamed: 1", because: "the file matching the literal pattern must be renamed");
        result.Output.Should().Contain("Failed: 0");
        File.Exists(Path.Combine(tempDir, "report.new.csv")).Should().BeTrue(
            because: "the renamed file must exist on disk with the literal suffix removed");
        File.Exists(Path.Combine(tempDir, "report.new.(2024).csv")).Should().BeFalse(
            because: "the original filename must no longer exist after a successful rename");
    }

    // Test 2 — Two files that compute to the same output name both get renamed in a live run (auto-numbering)
    [Fact]
    public void BatchCollision_LiveRun_BothFilesRenameWithAutoNumbering()
    {
        // prefix_report.csv → replace "prefix_" with "" → report.csv
        // suffix_report.csv → replace "suffix_" with "" → report.csv  (collision → report(1).csv)
        var tempDir = CreateTempDir("prefix_report.csv", "suffix_report.csv");
        var tester = CreateTester();

        var result = tester.Run(tempDir, "--replace", "prefix_->", "--replace", "suffix_->");

        result.ExitCode.Should().Be(0, because: "both files must be renamed without error via auto-numbering");
        result.Output.Should().Contain("Renamed: 2", because: "both colliding files must be renamed (no skips)");
        result.Output.Should().Contain("Failed: 0");
        File.Exists(Path.Combine(tempDir, "report.csv")).Should().BeTrue(
            because: "the first resolved name must exist on disk");
        File.Exists(Path.Combine(tempDir, "report(1).csv")).Should().BeTrue(
            because: "the auto-numbered name for the colliding file must exist on disk");
        File.Exists(Path.Combine(tempDir, "prefix_report.csv")).Should().BeFalse(
            because: "the original first file must no longer exist");
        File.Exists(Path.Combine(tempDir, "suffix_report.csv")).Should().BeFalse(
            because: "the original second file must no longer exist");
    }

    // Test 3 — rename-failures.log pre-present in a directory is never included as a rename candidate
    [Fact]
    public void LogFileExcluded_EvenWhenPatternWouldMatchIt()
    {
        // "rename-failures.log" would match the pattern "--remove rename-" but must be excluded (RPRT-03).
        // "rename-failures_new.txt" is a normal file that IS subject to the pattern.
        var tempDir = CreateTempDir("rename-failures.log", "rename-failures_new.txt");
        var tester = CreateTester();

        var result = tester.Run(tempDir, "--remove", "rename-");

        result.ExitCode.Should().Be(0);
        // Log file must not appear in output as a rename source
        result.Output.Should().NotContain("rename-failures.log →",
            because: "the log file must be excluded from the rename batch even when the pattern matches its name");
        // Log file must remain unchanged on disk
        File.Exists(Path.Combine(tempDir, "rename-failures.log")).Should().BeTrue(
            because: "the log file must not be touched by the rename operation");
        // The non-log file must be processed
        (result.Output.Contains("rename-failures_new.txt") || result.Output.Contains("failures_new.txt")).Should().BeTrue(
            because: "the non-log file matching the pattern must be processed");
    }

    // Test 4 — Running against an empty directory exits 0 with Renamed: 0 and no crash
    [Fact]
    public void EmptyDirectory_ExitsZero_WithZeroRenamedCount()
    {
        var tempDir = CreateTempDir(); // no files
        var tester = CreateTester();

        var result = tester.Run(tempDir, "--remove", "_new");

        result.ExitCode.Should().Be(0, because: "an empty directory is valid input; the tool must exit cleanly");
        result.Output.Should().Contain("Renamed: 0", because: "no files were renamed in an empty directory");
    }

    // Test 5 — Unicode filenames (accented characters) are renamed successfully
    [Fact]
    public void UnicodeFilename_RenamedSuccessfully()
    {
        // "café_new.csv" contains an accented character (UTF-8). .NET 9 handles unicode filenames correctly.
        var tempDir = CreateTempDir("café_new.csv");
        var tester = CreateTester();

        var result = tester.Run(tempDir, "--remove", "_new");

        result.ExitCode.Should().Be(0, because: "unicode filenames are valid real-world inputs and must succeed");
        result.Output.Should().Contain("Renamed: 1", because: "the unicode filename must be renamed successfully");
        result.Output.Should().Contain("Failed: 0");
        File.Exists(Path.Combine(tempDir, "café.csv")).Should().BeTrue(
            because: "the renamed unicode file must exist on disk (rename must succeed, not fail gracefully)");
        File.Exists(Path.Combine(tempDir, "café_new.csv")).Should().BeFalse(
            because: "the original unicode filename must no longer exist after a successful rename");
    }

    // Test 6 — Long filenames (~245 chars) are processed without error
    [Fact]
    public void LongFilename_ProcessedWithoutError()
    {
        // Construct a 244-char filename: 240 'a' chars + "_new.csv" suffix.
        // This is well within Windows MAX_PATH 255 for the filename portion alone.
        var longBaseName = new string('a', 240);
        var longFileName = longBaseName + "_new.csv";
        var tempDir = CreateTempDir(longFileName);
        var tester = CreateTester();

        var result = tester.Run(tempDir, "--remove", "_new");

        result.ExitCode.Should().Be(0, because: "long filenames within Windows MAX_PATH limits must be processed without error");
        result.Output.Should().Contain("Renamed: 1", because: "the long filename must be renamed successfully");
        result.Output.Should().Contain("Failed: 0");
        File.Exists(Path.Combine(tempDir, longBaseName + ".csv")).Should().BeTrue(
            because: "the renamed long-filename file must exist on disk");
    }
}
