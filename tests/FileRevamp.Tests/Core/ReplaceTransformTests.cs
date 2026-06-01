using FileRevamp.Core;
using FluentAssertions;

namespace FileRevamp.Tests.Core;

public class ReplaceTransformTests
{
    /// <summary>
    /// Apply replaces all occurrences of "." with "-".
    /// </summary>
    [Fact]
    public void Apply_DotToHyphen_ReplacesAllOccurrences()
    {
        var transform = new ReplaceTransform(".", "-");
        transform.Apply("report.final.csv").Should().Be("report-final-csv");
    }

    /// <summary>
    /// Apply replaces all occurrences of "_" with " ".
    /// </summary>
    [Fact]
    public void Apply_UnderscoreToSpace_ReplacesAllOccurrences()
    {
        var transform = new ReplaceTransform("_", " ");
        transform.Apply("my_file_name").Should().Be("my file name");
    }

    /// <summary>
    /// Apply returns original string unchanged when there is no match.
    /// </summary>
    [Fact]
    public void Apply_NoMatch_ReturnsOriginalUnchanged()
    {
        var transform = new ReplaceTransform("xyz", "abc");
        transform.Apply("no_match.csv").Should().Be("no_match.csv");
    }

    /// <summary>
    /// Apply returns original string when the find string is not present.
    /// </summary>
    [Fact]
    public void Apply_NoDots_ReturnsOriginalUnchanged()
    {
        var transform = new ReplaceTransform(".", "-");
        transform.Apply("noDotsHere").Should().Be("noDotsHere");
    }

    /// <summary>
    /// Parse splits on first "->" and returns correct ReplaceTransform.
    /// </summary>
    [Fact]
    public void Parse_ValidOperand_ReturnCorrectTransform()
    {
        var transform = ReplaceTransform.Parse(".->-");
        transform.Apply("report.2024.csv").Should().Be("report-2024-csv");
    }

    /// <summary>
    /// Parse throws ArgumentException when operand has no "->".
    /// </summary>
    [Fact]
    public void Parse_MissingArrow_ThrowsArgumentException()
    {
        var act = () => ReplaceTransform.Parse("nodash");
        act.Should().Throw<ArgumentException>()
            .WithMessage("*old->new*");
    }
}
